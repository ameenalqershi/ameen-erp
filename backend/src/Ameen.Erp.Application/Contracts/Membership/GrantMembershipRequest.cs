using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record GrantMembershipRequest(string Subject, string[] Permissions, bool AllBranches, Guid[] BranchIds, Guid? ExpectedVersion);
