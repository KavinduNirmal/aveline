/// Port for the auth token used to authorize outgoing API requests.
///
/// Defined in `core/network` so HTTP infrastructure depends on an
/// abstraction; the concrete implementation lives in the auth feature
/// (`features/auth/data/`).
abstract interface class AuthTokenProvider {
  /// Returns the current JWT, fetching or renewing it when needed.
  ///
  /// Returns `null` when there is no active session.
  Future<String?> getToken();

  /// Discards any cached token and fetches a fresh one.
  ///
  /// Returns `null` when there is no active session.
  Future<String?> refreshToken();

  /// Ends the current session.
  ///
  /// Invoked when a request is still rejected with 401 after a token refresh.
  Future<void> signOut();
}
