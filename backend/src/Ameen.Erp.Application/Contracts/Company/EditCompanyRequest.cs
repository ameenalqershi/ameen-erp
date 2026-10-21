using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record EditCompanyRequest(Guid ExpectedVersion, string Code, string Name, string CountryCode, string FunctionalCurrency, CompanyProfile Profile, string Reason);
