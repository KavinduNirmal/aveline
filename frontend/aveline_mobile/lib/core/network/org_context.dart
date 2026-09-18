/// The signed-in account has no organization context yet.
///
/// Every org-scoped route is `/api/v1/orgs/{organizationId}/…`, and the canonical
/// id arrives from `GET /orgs/my`, after the shell mounts. Until it does there is
/// nothing to call — but that is a "not yet", not a failure: the screen keeps its
/// loading state rather than showing an error card. The error state is reserved
/// for the server's own refusal (a `403` for a caller with no active membership).
///
/// Shared by every org-scoped feature so the distinction is made once, in the
/// same words, on Home and in the inbox.
class OrgContextUnavailable implements Exception {
  const OrgContextUnavailable();

  @override
  String toString() => 'The boutique is still loading.';
}
