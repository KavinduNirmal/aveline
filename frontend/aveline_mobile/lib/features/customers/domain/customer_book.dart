import 'customer.dart';

/// One letter's worth of the book.
class CustomerSection {
  const CustomerSection({required this.letter, required this.customers});

  /// `A` to `Z`, or `#` for the clients the boutique has no name for.
  final String letter;

  /// The clients filed under [letter], in the order the list prints them.
  final List<Customer> customers;
}

/// The client book under the narrowing in force, sectioned for the alphabet
/// index.
///
/// The whole book arrives at once rather than a page at a time: an alphabet index
/// promises that every letter is reachable, which a page cannot honour. The API's
/// customer lookup is a search rather than a paged feed, so this is also the
/// shape it hands back.
class CustomerBook {
  const CustomerBook(this.sections);

  /// Nothing matches the narrowing: no sections, and so no index.
  static const CustomerBook empty = CustomerBook(<CustomerSection>[]);

  final List<CustomerSection> sections;

  /// How many clients the book holds.
  int get total =>
      sections.fold(0, (count, section) => count + section.customers.length);

  bool get isEmpty => total == 0;

  /// The letters the index offers, in the order the book files them: `A` to `Z`
  /// with `#` last, and no letter that has nobody under it.
  List<String> get letters => [
    for (final section in sections)
      if (section.customers.isNotEmpty) section.letter,
  ];
}
