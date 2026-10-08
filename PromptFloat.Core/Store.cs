using Microsoft.Data.Sqlite;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace PromptFloat.Core;

public sealed class Store
{
    public const string Uncategorized = "uncategorized";
    public static readonly JsonSerializerOptions Json = new() { WriteIndented=true, PropertyNameCaseInsensitive=true, DefaultIgnoreCondition=JsonIgnoreCondition.WhenWritingNull, Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    public string Root { get; }
    public string DatabasePath => Path.Combine(Root,"library.db");
    public string SettingsPath => Path.Combine(Root,"settings.json");
    public string BackupsPath => Path.Combine(Root,"backups");
    public Settings Settings { get; private set; }
    public Store(string root)
    {
        Root=Path.GetFullPath(root); Directory.CreateDirectory(Root);
        var existed=File.Exists(DatabasePath);
        Settings = File.Exists(SettingsPath) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath),Json) ?? throw new InvalidDataException("设置文件为空") : new Settings();
        ValidateSettings(Settings);
        using var db=Open();
        using var command=db.CreateCommand();
        command.CommandText="PRAGMA user_version;";
        var version=Convert.ToInt32(command.ExecuteScalar());
        if(version>1) throw new InvalidDataException("数据库由更新版本创建，请升级软件后打开。");
        if(version==0&&existed)Backup("special");
        command.CommandText="CREATE TABLE IF NOT EXISTS categories(id TEXT PRIMARY KEY,data TEXT NOT NULL); CREATE TABLE IF NOT EXISTS prompts(id TEXT PRIMARY KEY,category_id TEXT NOT NULL,data TEXT NOT NULL); CREATE TABLE IF NOT EXISTS operations(id TEXT PRIMARY KEY,template_id TEXT NOT NULL,kind TEXT NOT NULL);";command.ExecuteNonQuery();
        command.CommandText="PRAGMA user_version=1; INSERT OR IGNORE INTO categories(id,data) VALUES($id,$data);";
        command.Parameters.AddWithValue("$id",Uncategorized);
        command.Parameters.AddWithValue("$data",JsonSerializer.Serialize(new Category {Id=Uncategorized,Name="未分类",Order=int.MaxValue},Json));
        command.ExecuteNonQuery();
    }
    private SqliteConnection Open()
    {
        var db=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=DatabasePath,Pooling=false,DefaultTimeout=3 }.ToString());
        db.Open(); return db;
    }
    public Library Read()
    {
        using var db=Open(); return Read(db);
    }
    private static Library Read(SqliteConnection db, SqliteTransaction? transaction=null)
    {
        var data=new Library();
        using var command=db.CreateCommand(); command.Transaction=transaction;
        command.CommandText="SELECT data FROM categories";
        using(var rows=command.ExecuteReader()) while(rows.Read()) data.Categories.Add(JsonSerializer.Deserialize<Category>(rows.GetString(0),Json)!);
        command.CommandText="SELECT data FROM prompts";
        using(var rows=command.ExecuteReader()) while(rows.Read()) data.Prompts.Add(JsonSerializer.Deserialize<Prompt>(rows.GetString(0),Json)!);
        return data;
    }
    public void Change(Action<Library> change)
    {
        var data=Read(); change(data); Validate(data);
        BeforeChange();
        using var db=Open(); using var transaction=db.BeginTransaction(); Write(db,transaction,data); transaction.Commit();
    }
    private static void Write(SqliteConnection db,SqliteTransaction transaction,Library data)
    {
        using var command=db.CreateCommand(); command.Transaction=transaction;
        command.CommandText="DELETE FROM categories; DELETE FROM prompts;"; command.ExecuteNonQuery();
        foreach(var category in data.Categories)
        {
            command.Parameters.Clear(); command.CommandText="INSERT INTO categories(id,data) VALUES($id,$data)";
            command.Parameters.AddWithValue("$id",category.Id); command.Parameters.AddWithValue("$data",JsonSerializer.Serialize(category,Json)); command.ExecuteNonQuery();
        }
        foreach(var prompt in data.Prompts)
        {
            command.Parameters.Clear(); command.CommandText="INSERT INTO prompts(id,category_id,data) VALUES($id,$category,$data)";
            command.Parameters.AddWithValue("$id",prompt.Id); command.Parameters.AddWithValue("$category",prompt.CategoryId); command.Parameters.AddWithValue("$data",JsonSerializer.Serialize(prompt,Json)); command.ExecuteNonQuery();
        }
    }
    public void SavePrompt(Prompt prompt,Category? newCategory=null)
    {
        if(string.IsNullOrWhiteSpace(prompt.Title)||string.IsNullOrWhiteSpace(prompt.Body)) throw new ArgumentException("标题和正文不能为空。");
        prompt.Modified=DateTimeOffset.UtcNow; prompt.Tags=prompt.Tags.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        prompt.Variables=TemplateEngine.Parse(prompt.Body,prompt.Variables);
        Change(data => { if(newCategory!=null){newCategory.Order=data.Categories.Count;data.Categories.Add(newCategory);} var index=data.Prompts.FindIndex(p=>p.Id==prompt.Id); if(index<0) {prompt.Order=data.Prompts.Count;data.Prompts.Add(prompt.Clone());} else data.Prompts[index]=prompt.Clone(); });
    }
    public void Batch(IEnumerable<string> ids, Action<Prompt> change)
    {
        var set=ids.ToHashSet();
        Change(data => { foreach(var p in data.Prompts.Where(p=>set.Contains(p.Id))) {change(p);p.Modified=DateTimeOffset.UtcNow;} });
    }
    public bool RecordUse(string id,string operationId,bool copied)
    {
        BeforeChange();
        using var db=Open(); using var tx=db.BeginTransaction(); using var cmd=db.CreateCommand(); cmd.Transaction=tx;
        cmd.CommandText="SELECT data FROM prompts WHERE id=$id"; cmd.Parameters.AddWithValue("$id",id);
        var json=cmd.ExecuteScalar() as string; if(json==null) return false;
        var p=JsonSerializer.Deserialize<Prompt>(json,Json)!; if(p.Deleted!=null || !p.Enabled) return false;
        cmd.CommandText="INSERT OR IGNORE INTO operations(id,template_id,kind) VALUES($op,$id,$kind)";
        cmd.Parameters.AddWithValue("$op",operationId); cmd.Parameters.AddWithValue("$kind",copied?"copy":"insert");
        if(cmd.ExecuteNonQuery()==0) return false;
        if(copied) p.CopyCount++; else p.InsertCount++; p.LastUsed=DateTimeOffset.UtcNow;
        cmd.CommandText="UPDATE prompts SET data=$data WHERE id=$id"; cmd.Parameters.AddWithValue("$data",JsonSerializer.Serialize(p,Json)); cmd.ExecuteNonQuery(); tx.Commit(); return true;
    }
    public void DeleteCategory(string id, bool deletePrompts=false, string? destination=null)
    {
        if(id==Uncategorized) throw new ArgumentException("未分类不能删除。");
        Change(data => {
            var target=destination??Uncategorized;
            if(target==id||!data.Categories.Any(c=>c.Id==target)) throw new ArgumentException("目标分类无效。");
            foreach(var p in data.Prompts.Where(p=>p.CategoryId==id)) {p.CategoryId=target;if(deletePrompts)p.Deleted=DateTimeOffset.UtcNow;}
            data.Categories.RemoveAll(c=>c.Id==id);
        });
    }
    public void PurgeTrash()
    {
        bool exists;
        using(var db=Open())using(var cmd=db.CreateCommand()){cmd.CommandText="SELECT EXISTS(SELECT 1 FROM prompts WHERE julianday(json_extract(data,'$.Deleted')) < julianday('now','-30 days'))";exists=Convert.ToInt32(cmd.ExecuteScalar())!=0;}
        if(exists)Change(l=>l.Prompts.RemoveAll(p=>p.Deleted<DateTimeOffset.UtcNow.AddDays(-30)));
    }
    public void AddBuiltIns(IEnumerable<string> sourceCategories)
    {
        var selected=sourceCategories.ToHashSet(); var built=Catalog.Create();
        Change(data=>{
            foreach(var c in built.Categories.Where(c=>selected.Contains(c.SourceId!)))
            {
                var target=data.Categories.FirstOrDefault(x=>x.SourceId==c.SourceId);
                if(target==null) {target=c; target.Order=data.Categories.Count-1; data.Categories.Add(target);}
                foreach(var p in built.Prompts.Where(p=>p.CategoryId==c.Id))
                    if(!data.Prompts.Any(x=>x.SourceId==p.SourceId)) {p.CategoryId=target.Id;data.Prompts.Add(p);}
            }
        });
    }
    public string Export(IEnumerable<string>? selectedIds=null,bool statistics=false)
    {
        var data=Read(); var set=selectedIds?.ToHashSet();
        var prompts=data.Prompts.Where(p=>p.Deleted==null&&(set==null||set.Contains(p.Id))).Select(p=>p.Clone()).ToList();
        if(!statistics) foreach(var p in prompts) {p.InsertCount=0;p.CopyCount=0;p.LastUsed=null;}
        var categories=data.Categories.Where(c=>set==null||prompts.Any(p=>p.CategoryId==c.Id)).ToList();
        return JsonSerializer.Serialize(new Exchange {IncludesStatistics=statistics,Categories=categories,Prompts=prompts},Json);
    }
    public ImportPreview PreviewImport(string json)
    {
        if(json.Length>40_000_000) throw new InvalidDataException("文件过大（上限 40 MB）。");
        Exchange incoming;
        try {incoming=JsonSerializer.Deserialize<Exchange>(json,Json)??throw new InvalidDataException("文件为空。");}
        catch(JsonException) {throw new InvalidDataException("不是有效的模板 JSON 文件。");}
        if(incoming.FormatVersion!=1) throw new InvalidDataException("不支持的导入格式版本。");
        Validate(new Library {Categories=incoming.Categories,Prompts=incoming.Prompts},false);
        var local=Read(); var keys=local.Prompts.Select(p=>TemplateEngine.BodyKey(p.Body)).ToHashSet();
        return new(incoming,incoming.Categories.Count,incoming.Prompts.Count,incoming.Prompts.Count(p=>local.Prompts.Any(x=>x.Id==p.Id)),incoming.Prompts.Count(p=>keys.Contains(TemplateEngine.BodyKey(p.Body))));
    }
    public void Import(ImportPreview preview,ConflictPolicy policy,bool keepStatistics=false)
    {
        // Revalidate at the trust boundary, before backup or mutation.
        Validate(new Library {Categories=preview.Data.Categories,Prompts=preview.Data.Prompts},false);
        Backup("special");
        Change(data=>{
            var mapping=new Dictionary<string,string>();
            foreach(var c in preview.Data.Categories)
            {
                if(c.Id==Uncategorized) {mapping[c.Id]=Uncategorized;continue;}
                var existing=data.Categories.FirstOrDefault(x=>x.Id==c.Id);
                if(existing!=null && policy==ConflictPolicy.Skip) {mapping[c.Id]=existing.Id;continue;}
                var copy=JsonSerializer.Deserialize<Category>(JsonSerializer.Serialize(c,Json),Json)!;
                if(existing!=null && policy==ConflictPolicy.NewCopy) {copy.Id=Guid.NewGuid().ToString("N");copy.SourceId=null;}
                if(existing!=null && policy==ConflictPolicy.Overwrite) data.Categories.Remove(existing);
                mapping[c.Id]=copy.Id;data.Categories.Add(copy);
            }
            foreach(var original in preview.Data.Prompts)
            {
                var existing=data.Prompts.FirstOrDefault(p=>p.Id==original.Id);
                if(existing!=null&&policy==ConflictPolicy.Skip) continue;
                var p=original.Clone();p.CategoryId=mapping[p.CategoryId];p.Source="import";p.Deleted=null;
                if(!keepStatistics) {p.InsertCount=0;p.CopyCount=0;p.LastUsed=null;}
                if(existing!=null&&policy==ConflictPolicy.NewCopy) {p.Id=Guid.NewGuid().ToString("N");p.SourceId=null;}
                if(existing!=null&&policy==ConflictPolicy.Overwrite) data.Prompts.Remove(existing);
                data.Prompts.Add(p);
            }
        });
    }
    private void BeforeChange()
    {
        if(!Settings.AutoBackup) return;
        var prefix="daily-"+DateTime.Now.ToString("yyyyMMdd");
        if(!Directory.Exists(BackupsPath)||!Directory.EnumerateFiles(BackupsPath,prefix+"*.zip").Any()) Backup("daily");
    }
    public string Backup(string kind="special")
    {
        Directory.CreateDirectory(BackupsPath);
        var file=Path.Combine(BackupsPath,$"{kind}-{DateTime.Now:yyyyMMdd-HHmmss-fffffff}.zip");
        var temp=Path.Combine(Root,Guid.NewGuid().ToString("N")+".db.tmp");
        try
        {
            using(var source=Open()) using(var target=new SqliteConnection($"Data Source={temp};Pooling=False")) {target.Open();source.BackupDatabase(target);}
            using(var zip=ZipFile.Open(file,ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(temp,"library.db");
                using var writer=new StreamWriter(zip.CreateEntry("settings.json").Open());writer.Write(JsonSerializer.Serialize(Settings,Json));
            }
            foreach(var old in Directory.GetFiles(BackupsPath,kind+"-*.zip").OrderByDescending(x=>x).Skip(kind=="daily"?7:3)) File.Delete(old);
            return file;
        }
        catch {if(File.Exists(file))File.Delete(file);throw;}
        finally {if(File.Exists(temp))File.Delete(temp);}
    }
    public void Restore(string file)
    {
        var temp=Path.Combine(Root,Guid.NewGuid().ToString("N")+".restore");
        try
        {
            Settings settings;
            using(var zip=ZipFile.OpenRead(file))
            {
            var database=zip.GetEntry("library.db")??throw new InvalidDataException("备份缺少数据库。");
            var settingsEntry=zip.GetEntry("settings.json")??throw new InvalidDataException("备份缺少设置。");
            if(database.Length>100_000_000||settingsEntry.Length>1_000_000) throw new InvalidDataException("备份大小异常。");
            using var reader=new StreamReader(settingsEntry.Open());settings=JsonSerializer.Deserialize<Settings>(reader.ReadToEnd(),Json)??throw new InvalidDataException("设置无效。");ValidateSettings(settings);
            database.ExtractToFile(temp);
            }
            using(var candidate=new SqliteConnection($"Data Source={temp};Mode=ReadOnly;Pooling=False"))
            {
                candidate.Open();using var check=candidate.CreateCommand();check.CommandText="PRAGMA integrity_check";
                if((string?)check.ExecuteScalar()!="ok") throw new InvalidDataException("备份数据库损坏。");
                check.CommandText="PRAGMA user_version";if(Convert.ToInt32(check.ExecuteScalar())!=1)throw new InvalidDataException("不支持的数据库版本。");
                Validate(Read(candidate));
            }
            Backup("special");
            var settingsTemp=SettingsPath+".tmp";File.WriteAllText(settingsTemp,JsonSerializer.Serialize(settings,Json));
            var rollback=DatabasePath+".rollback";File.Copy(DatabasePath,rollback,true);
            try {File.Move(temp,DatabasePath,true);File.Move(settingsTemp,SettingsPath,true);Settings=settings;}
            catch {File.Move(rollback,DatabasePath,true);throw;}
            finally {if(File.Exists(rollback))File.Delete(rollback);if(File.Exists(settingsTemp))File.Delete(settingsTemp);}
        }
        finally {if(File.Exists(temp))File.Delete(temp);}
    }
    public void SaveSettings(Settings settings)
    {
        ValidateSettings(settings);var temp=SettingsPath+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(settings,Json));File.Move(temp,SettingsPath,true);Settings=settings;
    }
    public static void ValidateSettings(Settings s)
    {
        if(s.Agents==null||s.Agents.Count>40||s.Agents.Any(a=>a==null)||s.Agents.Select(a=>a.Id).Distinct().Count()!=s.Agents.Count)throw new InvalidDataException("智能体配置无效。");
        foreach(var a in s.Agents)PolishService.Validate(a);
        if(s.DefaultAgentId!=null&&!s.Agents.Any(a=>a.Id==s.DefaultAgentId))throw new InvalidDataException("默认智能体不存在。");
        if(!new[]{s.BallX,s.BallY,s.BallSize,s.Opacity,s.PanelWidth,s.PanelHeight}.All(double.IsFinite)||s.BallSize<28||s.BallSize>88||s.Opacity<.2||s.Opacity>1||s.PanelWidth<480||s.PanelWidth>1600||s.PanelHeight<360||s.PanelHeight>1200||s.CopyOnlyApps==null||s.CopyOnlyApps.Any(x=>x==null)||string.IsNullOrWhiteSpace(s.PanelHotkey)||string.IsNullOrWhiteSpace(s.SaveHotkey)||string.IsNullOrWhiteSpace(s.HideHotkey)||s.Theme is not ("浅色" or "深色")) throw new InvalidDataException("设置数值无效。");
    }
    private static void Validate(Library data,bool requireUncategorized=true)
    {
        if(data.Categories==null||data.Prompts==null||data.Categories.Count>1000||data.Prompts.Count>20000) throw new InvalidDataException("模板库大小或结构无效。");
        if(data.Categories.Any(c=>c==null||string.IsNullOrWhiteSpace(c.Id)||string.IsNullOrWhiteSpace(c.Name)||c.Name.Length>100||c.Description==null)||data.Categories.Select(c=>c.Id).Distinct().Count()!=data.Categories.Count)throw new InvalidDataException("分类无效或 ID 重复。");
        if(requireUncategorized&&!data.Categories.Any(c=>c.Id==Uncategorized))throw new InvalidDataException("缺少未分类。");
        var ids=data.Categories.Select(c=>c.Id).ToHashSet();
        foreach(var p in data.Prompts)
            if(p==null||string.IsNullOrWhiteSpace(p.Id)||!ids.Contains(p.CategoryId)||string.IsNullOrWhiteSpace(p.Title)||p.Title.Length>100||string.IsNullOrWhiteSpace(p.Body)||p.Body.Length>100000||p.Note==null||p.Tags==null||p.Tags.Any(t=>t==null)||p.Variables==null||p.Variables.Any(v=>v==null||v.Name==null||v.Default==null)||p.InsertCount<0||p.CopyCount<0||p.InsertCount>long.MaxValue-p.CopyCount)throw new InvalidDataException("模板字段无效或引用不存在的分类。");
        if(data.Prompts.Select(p=>p.Id).Distinct().Count()!=data.Prompts.Count)throw new InvalidDataException("模板 ID 重复。");
    }
}
