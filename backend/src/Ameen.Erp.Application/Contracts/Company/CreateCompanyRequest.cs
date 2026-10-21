using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record CreateCompanyRequest(Guid Id, string Code, string Name, string CountryCode, string FunctionalCurrency, int CurrencyScale, CompanyProfile Profile);
