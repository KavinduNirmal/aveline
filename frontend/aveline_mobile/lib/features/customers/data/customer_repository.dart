import '../domain/customer_book.dart';
import '../domain/customer_detail.dart';
import '../domain/customer_level.dart';

/// The narrowing the book is fetched under.
class CustomerQuery {
  const CustomerQuery({this.search = '', this.level});

  /// Free text from the book's own search field.
  final String search;

  /// The level the book is narrowed to, or `null` for every level.
  final CustomerLevel? level;

  /// Whether nothing is narrowing the book, so the whole shop is in scope.
  bool get isEmpty => search.trim().isEmpty && level == null;
}

/// Fetches the boutique's client book.
///
/// One call returns the whole narrowing rather than a page, because the alphabet
/// index has to be able to reach every letter it offers. The lookup behind it
/// (`POST /internal/customers/lookup`) is a search, not a feed, which is the same
/// shape.
abstract interface class CustomerRepository {
  Future<CustomerBook> fetchBook({CustomerQuery query = const CustomerQuery()});

  /// Fetches one client's whole profile, or `null` when the shop has no such
  /// client.
  ///
  /// The information screen resolves through this rather than trusting the
  /// client it was handed: the router re-parses a location whenever the auth or
  /// profile listenable fires, and any `extra` a push carried does not survive
  /// that. Resolving from the id makes the screen a pure function of the
  /// location.
  Future<CustomerDetail?> fetchCustomer(String id);
}
