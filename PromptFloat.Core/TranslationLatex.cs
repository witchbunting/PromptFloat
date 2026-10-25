using System.Text;
namespace PromptFloat.Core;

public sealed record TranslationDocument(string Source,IReadOnlyList<string> Warnings);
public static class TranslationLatex
{
    private static readonly HashSet<string> Commands=new((
        "alpha beta gamma delta epsilon varepsilon zeta eta theta vartheta iota kappa lambda mu nu xi pi varpi rho varrho sigma varsigma tau upsilon phi varphi chi psi omega "+
        "Gamma Delta Theta Lambda Xi Pi Sigma Upsilon Phi Psi Omega "+
        "frac dfrac tfrac cfrac sqrt sum prod coprod int iint iiint oint lim limsup liminf inf sup min max arg "+
        "sin cos tan cot sec csc arcsin arccos arctan sinh cosh tanh exp log ln det dim ker gcd deg "+
        "left right middle big Big bigg Bigg bigl bigr Bigl Bigr biggl biggr Biggl Biggr "+
        "mathrm mathit mathbf mathsf mathtt mathcal mathbb mathfrak boldsymbol bm text textrm textsf texttt textnormal textbf textit operatorname "+
        "overline underline widehat widetilde hat tilde bar vec dot ddot dddot acute grave breve check "+
        "underbrace overbrace overset underset stackrel substack binom dbinom tbinom "+
        "infty partial nabla ell hbar imath jmath Re Im emptyset varnothing forall exists nexists neg "+
        "in notin ni subset supset subseteq supseteq nsubseteq nsupseteq cup cap bigcup bigcap setminus "+
        "le leq ge geq neq ne approx simeq sim equiv propto cong prec succ preceq succeq "+
        "times cdot div pm mp circ bullet ast star otimes oplus odot bigotimes bigoplus wedge vee land lor "+
        "to mapsto rightarrow leftarrow leftrightarrow Rightarrow Leftarrow Leftrightarrow longrightarrow longleftarrow "+
        "Longrightarrow Longleftarrow Longleftrightarrow implies iff uparrow downarrow "+
        "langle rangle lvert rvert lVert rVert vert Vert mid parallel perp angle triangle square Box "+
        "cdots ldots dots vdots ddots quad qquad enspace thinspace negthinspace "+
        "displaystyle textstyle scriptstyle scriptscriptstyle limits nolimits "+
        "begin end multicolumn hline not phantom vphantom hphantom smash mathrel mathop mathbin mathord "+
        "ket bra braket Tr tr").Split(' ',StringSplitOptions.RemoveEmptyEntries),StringComparer.Ordinal);
    private static readonly HashSet<string> Environments=new(["aligned","alignedat","gathered","split","array","cases","matrix","pmatrix","bmatrix","Bmatrix","vmatrix","Vmatrix","smallmatrix"],StringComparer.Ordinal);
    public static TranslationDocument Build(string translation,string language)
    {
        if(string.IsNullOrWhiteSpace(translation)||translation.Length>100000)throw new ArgumentException("请先生成或填写译文（最多 100,000 字符）。");
        if(string.IsNullOrWhiteSpace(language)||language.Length>80||language.Any(char.IsControl))throw new ArgumentException("目标语言无效。");
        translation=translation.Replace("\r\n","\n").Replace('\r','\n');
        var body=new StringBuilder();var plain=new StringBuilder();var warnings=new List<string>();
        void Flush(){body.Append(Escape(plain.ToString()));plain.Clear();}
        for(var i=0;i<translation.Length;)
        {
            string? open=null,close=null,environment=null;var display=false;
            if(translation.AsSpan(i).StartsWith(@"\(")){open=@"\(";close=@"\)";}
            else if(translation.AsSpan(i).StartsWith(@"\[")){open=@"\[";close=@"\]";display=true;}
            else if(translation.AsSpan(i).StartsWith("$$")){open="$$";close="$$";display=true;}
            else if(translation[i]=='$'&&(i==0||translation[i-1]!='\\')){open="$";close="$";}
            else
                foreach(var env in new[]{"equation*","equation","align*","align","gather*","gather"})
                    if(translation.AsSpan(i).StartsWith(@"\begin{"+env+"}"))
                    {open=@"\begin{"+env+"}";close=@"\end{"+env+"}";environment=env.StartsWith("align")?"aligned":env.StartsWith("gather")?"gathered":null;display=true;break;}
            if(open==null){plain.Append(translation[i++]);continue;}
            var end=translation.IndexOf(close!,i+open.Length,StringComparison.Ordinal);
            while(close=="$"&&end>0&&translation[end-1]=='\\')end=translation.IndexOf(close,end+1,StringComparison.Ordinal);
            if(end<0){warnings.Add("有未闭合的公式标记，已按文字显示。");plain.Append(translation.AsSpan(i));break;}
            Flush();var math=translation.Substring(i+open.Length,end-i-open.Length);
            if(SafeMath(math,out var normalized))
            {
                body.Append(display?@"\[":@"\(");
                if(environment!=null)body.Append(@"\begin{"+environment+"}");
                body.Append(normalized);
                if(environment!=null)body.Append(@"\end{"+environment+"}");
                body.Append(display?@"\]":@"\)");
            }
            else {warnings.Add("有公式包含未支持的命令或结构，已按原样文字显示。");body.Append(Escape(translation.Substring(i,end+close!.Length-i)));}
            i=end+close!.Length;
        }
        Flush();
        var header=@"\documentclass[UTF8,fontset=fandol,11pt]{ctexart}
\usepackage[a4paper,margin=22mm]{geometry}
\usepackage{amsmath,amssymb,bm,mathtools}
\newcommand{\ket}[1]{\left|#1\right\rangle}
\newcommand{\bra}[1]{\left\langle#1\right|}
\newcommand{\braket}[1]{\left\langle#1\right\rangle}
\DeclareMathOperator{\Tr}{Tr}
\DeclareMathOperator{\tr}{tr}
\setlength{\parindent}{0pt}
\setlength{\parskip}{0.5em}
\pagestyle{plain}
\begin{document}
\section*{译文 · "+Escape(language)+"}\n";
        return new TranslationDocument(header+body+"\n\\end{document}\n",warnings.Distinct().ToArray());
    }
    public static string Escape(string text)
    {
        var output=new StringBuilder();
        foreach(var c in text)
            output.Append(c switch {
                '\\'=>@"\textbackslash{}",'{'=>@"\{",'}'=>@"\}",'$'=>@"\$",'&'=>@"\&",'#'=>@"\#",
                '_'=>@"\_",'%'=>@"\%",'^'=>@"\textasciicircum{}",'~'=>@"\textasciitilde{}",
                '\n' or '\u2028' or '\u2029'=>"\n\\par\n",'\t'=>"    ",_=>char.IsControl(c)?"":c.ToString()});
        return output.ToString();
    }
    private static bool SafeMath(string text,out string normalized)
    {
        normalized="";var output=new StringBuilder();var depth=0;var envs=new Stack<string>();
        for(var i=0;i<text.Length;i++)
        {
            var c=text[i];
            if(c=='^'&&i+1<text.Length&&text[i+1]=='^'||c=='$'||char.IsControl(c)&&!char.IsWhiteSpace(c))return false;
            if(c=='{'){if(++depth>64)return false;}
            if(c=='}'&&--depth<0)return false;
            if(c=='\\')
            {
                if(++i>=text.Length)return false;
                var start=i;
                while(i<text.Length&&text[i] is >= 'a' and <= 'z' or >= 'A' and <= 'Z')i++;
                if(start==i)
                {
                    if(!"{}|,;:! %_#$&\\".Contains(text[i]))return false;
                    output.Append('\\').Append(text[i]);continue;
                }
                var command=text[start..i];
                if(!Commands.Contains(command))return false;
                output.Append('\\').Append(command);i--;
                if(command is "begin" or "end")
                {
                    var from=i+1;
                    if(from>=text.Length||text[from]!='{')return false;
                    var to=text.IndexOf('}',from+1);if(to<0)return false;
                    var env=text[(from+1)..to];if(!Environments.Contains(env))return false;
                    if(command=="begin"){if(envs.Count>=32)return false;envs.Push(env);}
                    else if(envs.Count==0||envs.Pop()!=env)return false;
                    output.Append('{').Append(env).Append('}');i=to;
                }
                continue;
            }
            if(c=='%')output.Append(@"\%");
            else if(c=='#')output.Append(@"\#");
            else output.Append(c);
        }
        if(depth!=0||envs.Count!=0)return false;
        normalized=output.ToString();return true;
    }
}
