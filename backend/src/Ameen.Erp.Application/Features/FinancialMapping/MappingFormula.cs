using Ameen.Erp.Domain;
namespace Ameen.Erp.Application;
internal static class MappingFormula
{
    // Safe grammar: [ITEM_CODE], +, -, parentheses and unary signs. Never evaluates arbitrary code.
    public static IReadOnlyList<(string Code,int Sign)> Parse(string formula)
    {
        var p=0;var references=0;
        void Skip(){while(p<formula.Length && char.IsWhiteSpace(formula[p]))p++;}
        List<(string Code,int Sign)> Factor(int depth)
        {
            Check.That(depth<=32,"mapping_formula_depth","المعادلة أعمق من المسموح.");Skip();var sign=1;
            while(p<formula.Length && (formula[p]=='+' || formula[p]=='-')){if(formula[p++]=='-')sign=-sign;Skip();}
            List<(string Code,int Sign)> value;
            if(p<formula.Length && formula[p]=='(')
            {p++;value=Expression(depth+1);Skip();Check.That(p<formula.Length && formula[p++]==')',"invalid_mapping_formula","قوس غير مغلق.");}
            else
            {
                Check.That(p<formula.Length && formula[p++]=='[',"invalid_mapping_formula","استخدم مراجع البنود بين أقواس مربعة فقط.");
                var start=p;while(p<formula.Length && formula[p]!=']')p++;
                Check.That(p<formula.Length,"invalid_mapping_formula","مرجع غير مغلق.");var code=Check.Code(formula[start..p++],"مرجع البند");
                Check.That(++references<=1000,"mapping_formula_size","مراجع المعادلة أكثر من المسموح.");value=new(){(code,1)};
            }
            return value.Select(x=>(x.Code,x.Sign*sign)).ToList();
        }
        List<(string Code,int Sign)> Expression(int depth)
        {
            var result=Factor(depth);Skip();
            while(p<formula.Length && formula[p]!=')')
            {
                Check.That(formula[p] is '+' or '-',"invalid_mapping_formula","عامل معادلة غير مسموح.");var sign=formula[p++]=='-'?-1:1;
                result.AddRange(Factor(depth).Select(x=>(x.Code,x.Sign*sign)));Skip();
            }
            return result;
        }
        var parsed=Expression(0);Skip();Check.That(p==formula.Length,"invalid_mapping_formula","نهاية معادلة غير صالحة.");return parsed;
    }
}
