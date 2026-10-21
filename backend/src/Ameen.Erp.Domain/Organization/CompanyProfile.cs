namespace Ameen.Erp.Domain;

public sealed record CompanyProfile(string LegalName, string Jurisdiction, DateOnly FiscalYearStartsOn,
    string City, string Street, string? EnglishName = null, string? Notes = null, string? CommercialRegistration = null,
    string? TaxRegistration = null, string? District = null, string? BuildingNumber = null,
    string? PostalCode = null, string? AdditionalNumber = null);
