using Ameen.Erp.Domain;
namespace Ameen.Erp.Application;
internal static class FinancialMappingCompiler
{
    public static FinancialMappingEvaluation Evaluate(FinancialMapping map,IReadOnlyList<Account> accounts,
        IReadOnlyDictionary<Guid,decimal> balances,DateOnly asOf,bool requireActiveReferences,string functionalCurrency="")
    {
        var lines=map.Lines.ToDictionary(x=>x.Code,StringComparer.Ordinal);var catalog=accounts.ToDictionary(x=>x.Id);
        foreach(var line in map.Lines)
        {
            var parents=new HashSet<string>(StringComparer.Ordinal){line.Code};var parent=line.ParentCode;
            while(parent is not null)
            {
                Check.That(lines.TryGetValue(parent,out var row),"mapping_parent_not_found","بند أب غير موجود.");
                Check.That(parents.Add(parent),"mapping_parent_cycle","دورة في شجرة البنود.");parent=row!.ParentCode;
            }
        }
        var children=accounts.Where(x=>x.ParentId!=null).ToLookup(x=>x.ParentId!.Value);
        HashSet<Guid> Expand(Guid id)
        {
            Check.That(catalog.TryGetValue(id,out var account),"reference_not_found","الحساب أو المجموعة غير موجود ضمن الشركة.");
            Check.That(!requireActiveReferences || account!.IsActive,"mapping_account_inactive","الحساب أو المجموعة معطل.");
            var result=new HashSet<Guid>();var visited=new HashSet<Guid>();var stack=new Stack<Account>();stack.Push(account!);
            while(stack.Count>0)
            {
                var current=stack.Pop();Check.That(visited.Add(current.Id),"account_hierarchy_cycle","دورة في مجموعة الحسابات.");
                if(current.IsPosting)result.Add(current.Id);foreach(var child in children[current.Id])stack.Push(child);
            }
            Check.That(result.Count>0,"mapping_empty_account_group","لا تتضمن المجموعة حسابات حركة.");return result;
        }
        var direct=new Dictionary<string,HashSet<Guid>>(StringComparer.Ordinal);var classified=new HashSet<Guid>();
        foreach(var line in map.Lines.Where(x=>x.AccountId!=null))
        {
            var expanded=Expand(line.AccountId!.Value);
            Check.That(!expanded.Any(classified.Contains),"duplicate_mapping_account","الحساب مصنف في أكثر من بند أو مجموعة متداخلة.");
            classified.UnionWith(expanded);direct[line.Code]=expanded;
        }
        var cache=new Dictionary<string,Dictionary<Guid,int>>(StringComparer.Ordinal);var visiting=new HashSet<string>(StringComparer.Ordinal);var contributionCount=0;
        Dictionary<Guid,int> Compile(string code,int depth)
        {
            Check.That(depth<=500,"mapping_formula_depth","عمق مراجع غير مسموح.");
            Check.That(lines.TryGetValue(code,out var line),"mapping_reference_not_found","المعادلة تشير لبند غير موجود.");
            if(cache.TryGetValue(code,out var saved))return saved;
            Check.That(visiting.Add(code),"mapping_formula_cycle","دورة في مراجع صيغ التجميع.");var value=new Dictionary<Guid,int>();
            if(direct.TryGetValue(code,out var ids))foreach(var id in ids)value[id]=1;
            if(line!.Formula is not null)
            {
                foreach(var (reference,sign) in MappingFormula.Parse(line.Formula))
                foreach(var contribution in Compile(reference,depth+1))
                    Check.That(value.TryAdd(contribution.Key,contribution.Value*sign),"duplicate_mapping_aggregation","المعادلة تحتسب الحساب أكثر من مرة ولو بعلامتين متعاكستين.");
            }
            foreach(var id in value.Keys.ToArray())value[id]*=(int)line.DisplaySign;
            contributionCount+=value.Count;Check.That(contributionCount<=200000,"mapping_complexity_limit","الخريطة تتجاوز حد التعقيد الآمن.");
            visiting.Remove(code);cache[code]=value;return value;
        }
        FinancialMappingRow[] rows;
        try
        {
            rows=map.Lines.OrderBy(x=>x.Position).Select(x=>
            {var c=Compile(x.Code,0);return new FinancialMappingRow(x.Code,x.ParentCode,x.Title,x.DisplaySign,x.Formula,
                c.Sum(a=>balances.GetValueOrDefault(a.Key)*a.Value),c.Keys.OrderBy(a=>a).ToArray());}).ToArray();
        }
        catch(OverflowException){throw new BusinessException("mapping_amount_overflow","مجموع الأرصدة يتجاوز الدقة المدعومة.");}
        var required=accounts.Where(x=>x.IsPosting && (x.IsActive || balances.ContainsKey(x.Id))).Select(x=>x.Id).ToHashSet();
        var issues=required.Where(x=>!classified.Contains(x)).OrderBy(x=>x).Select(x=>new FinancialMappingIssue("account_unclassified",x,"حساب حركة غير مصنف؛ يلزم ربطه قبل اعتماد الخريطة.")).ToArray();
        return new(map.Name,map.StatementKind,map.VersionCode,map.EffectiveFrom,asOf,functionalCurrency,issues.Length==0,required.Count,
            required.Count(classified.Contains),rows,issues);
    }
}
