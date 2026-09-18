import 'dart:math' as math;

import '../domain/customer.dart';
import '../domain/customer_book.dart';
import '../domain/customer_detail.dart';
import '../domain/customer_level.dart';
import 'customer_repository.dart';

/// A boutique's client book, held in memory.
///
/// Stands in for the customer lookup until the concierge slice is wired. It
/// mirrors `CustomerLoyaltyService`'s rule for the API's status and fills every
/// field of `CustomerProfileDto`, so the book it hands back is the book the API
/// will hand back — including the letters the alphabet index has to offer.
///
/// Each client's information screen is filled from the same pools the agent
/// service writes from (`MemoryCategory`, `MemorySource`, the occasions it
/// detects), picked from a generator seeded by the client's own id so a profile
/// reads the same on every run.
class DemoCustomerRepository implements CustomerRepository {
  DemoCustomerRepository({
    this.bookDelay = const Duration(milliseconds: 300),
  });

  /// How long the book takes to arrive.
  ///
  /// The pool is local, so without a delay the book would arrive instantly and
  /// the loading state would never be seen. Tests pass `Duration.zero`.
  final Duration bookDelay;

  List<Customer>? _pool;

  @override
  Future<CustomerBook> fetchBook({
    CustomerQuery query = const CustomerQuery(),
  }) async {
    if (bookDelay > Duration.zero) {
      await Future<void>.delayed(bookDelay);
    }

    return _section(_filter(_pool ??= _buildBook(), query));
  }

  @override
  Future<CustomerDetail?> fetchCustomer(String id) async {
    if (bookDelay > Duration.zero) {
      await Future<void>.delayed(bookDelay);
    }

    for (final customer in _pool ??= _buildBook()) {
      if (customer.id == id) {
        return _profileFor(customer);
      }
    }
    return null;
  }

  List<Customer> _filter(List<Customer> pool, CustomerQuery query) {
    final search = query.search.trim().toLowerCase();
    return pool.where((customer) {
      if (query.level != null && customer.level != query.level) {
        return false;
      }
      return search.isEmpty || customer.searchHaystack.contains(search);
    }).toList();
  }

  /// Groups the clients by the letter of their name, `A` to `Z` with the
  /// unnamed filing under `#` last.
  CustomerBook _section(List<Customer> matching) {
    final grouped = <String, List<Customer>>{};
    for (final customer in matching) {
      grouped.putIfAbsent(customer.sectionLetter, () => <Customer>[]).add(
        customer,
      );
    }

    final letters = grouped.keys.toList()
      ..sort((a, b) {
        // `#` is a section, not a letter, and a phone's contact list files it
        // after the alphabet rather than before it.
        if (a == '#') return b == '#' ? 0 : 1;
        if (b == '#') return -1;
        return a.compareTo(b);
      });

    return CustomerBook([
      for (final letter in letters)
        CustomerSection(
          letter: letter,
          customers: grouped[letter]!
            ..sort(
              (a, b) => a.displayName.toLowerCase().compareTo(
                b.displayName.toLowerCase(),
              ),
            ),
        ),
    ]);
  }

  /// The day this demo book was authored against.
  ///
  /// Fixed rather than `DateTime.now()`, so a client's recency — and therefore
  /// their status — reads the same on every run.
  static final DateTime _reference = DateTime.utc(2026, 9, 17);

  /// One client's profile, assembled the way the concierge assembles it: a
  /// preference list, a memory log, the occasions on file, the recent
  /// interactions, and the consent row.
  CustomerDetail _profileFor(Customer customer) {
    // Seeded from the id so the same client always has the same history.
    final random = math.Random(customer.id.hashCode);
    final days = [for (var i = 0; i < 6; i++) 6 + i * 7 + random.nextInt(5)];

    return CustomerDetail(
      customer: customer,
      preferences: _preferencesFor(customer, random),
      consent: _consentFor(customer, random),
      memories: [
        for (var i = 0; i < 3 + random.nextInt(3); i++)
          _memory(i, days[i % days.length]),
      ],
      events: _eventsFor(customer, random),
      interactions: [
        for (var i = 0; i < 3 + random.nextInt(3); i++)
          _interaction(i, hoursAgo: i * 34 + random.nextInt(20)),
      ],
    );
  }

  List<CustomerPreference> _preferencesFor(Customer customer, math.Random r) {
    final preferences = <CustomerPreference>[
      CustomerPreference(
        id: 'pref-colour-${customer.id}',
        key: 'Colour',
        value: _colours[r.nextInt(_colours.length)],
        isExplicit: true,
        confidence: 0.9,
      ),
      CustomerPreference(
        id: 'pref-fabric-${customer.id}',
        key: 'Fabric',
        value: _fabrics[r.nextInt(_fabrics.length)],
        isExplicit: r.nextBool(),
        confidence: 0.55 + r.nextInt(4) * 0.1,
      ),
      CustomerPreference(
        id: 'pref-size-${customer.id}',
        key: 'Size',
        value: _sizes[r.nextInt(_sizes.length)],
        isExplicit: true,
        confidence: 0.85,
      ),
    ];

    // The boutique's own tags are the clearest statement of what a client comes
    // in for, so they lead the list as an inferred preference.
    for (final tag in customer.tags) {
      preferences.insert(
        0,
        CustomerPreference(
          id: 'pref-tag-$tag-${customer.id}',
          key: 'Shops for',
          value: tag.replaceAll('-', ' '),
          isExplicit: false,
          confidence: 0.7,
        ),
      );
    }
    return preferences;
  }

  CustomerConsent _consentFor(Customer customer, math.Random r) {
    final status = switch (r.nextInt(10)) {
      0 => ConsentStatus.revoked,
      1 || 2 => ConsentStatus.pending,
      3 => ConsentStatus.unknown,
      _ => ConsentStatus.granted,
    };

    return CustomerConsent(
      status: status,
      grantedAtUtc: status == ConsentStatus.granted
          ? _reference.subtract(Duration(days: 40 + r.nextInt(120)))
          : null,
      revokedAtUtc: status == ConsentStatus.revoked
          ? _reference.subtract(Duration(days: 10 + r.nextInt(30)))
          : null,
    );
  }

  CustomerMemory _memory(int index, int daysAgo) {
    final entry = _memories[index % _memories.length];
    return CustomerMemory(
      id: 'mem-$index',
      content: entry.content,
      category: entry.category,
      source: entry.source,
      isExplicit: entry.isExplicit,
      confidence: entry.confidence,
      createdAtUtc: _reference.subtract(Duration(days: daysAgo)),
    );
  }

  List<CustomerEvent> _eventsFor(Customer customer, math.Random r) {
    if (r.nextInt(10) < 3) {
      return const <CustomerEvent>[];
    }

    final events = <CustomerEvent>[
      CustomerEvent(
        id: 'evt-1-${customer.id}',
        type: _occasions[r.nextInt(_occasions.length)],
        dateUtc: _reference.add(Duration(days: 4 + r.nextInt(40))),
        description: 'Wants something for the evening reception.',
      ),
    ];
    if (r.nextBool()) {
      events.add(
        CustomerEvent(
          id: 'evt-2-${customer.id}',
          type: CustomerEventType.birthday,
          dateUtc: _reference.subtract(Duration(days: 20 + r.nextInt(60))),
          isActive: false,
        ),
      );
    }
    return events;
  }

  CustomerInteraction _interaction(int index, {required int hoursAgo}) {
    final entry = _exchanges[index % _exchanges.length];
    return CustomerInteraction(
      id: 'int-$index',
      channel: entry.channel,
      direction: entry.direction,
      messageContent: entry.message,
      createdAtUtc: _reference.subtract(Duration(hours: hoursAgo)),
    );
  }

  static const List<String> _colours = [
    'Wine',
    'Ivory',
    'Emerald',
    'Midnight',
    'Champagne',
    'Saffron',
  ];

  static const List<String> _fabrics = [
    'Raw silk',
    'Handloom cotton',
    'Chiffon',
    'Velvet',
    'Organza',
  ];

  static const List<String> _sizes = ['36', '38', '40', '42', 'Custom'];

  static const List<CustomerEventType> _occasions = [
    CustomerEventType.wedding,
    CustomerEventType.anniversary,
    CustomerEventType.party,
    CustomerEventType.office,
    CustomerEventType.function,
  ];

  /// The kinds of thing the agent service writes into a client's memory.
  static const List<({
    String content,
    MemoryCategory category,
    MemorySource source,
    bool isExplicit,
    double confidence,
  })>
  _memories = [
    (
      content: 'Prefers ivory and raw silk, and dislikes heavy zari.',
      category: MemoryCategory.preference,
      source: MemorySource.conversation,
      isExplicit: true,
      confidence: 0.92,
    ),
    (
      content: 'Asked to be messaged before 6pm, never after.',
      category: MemoryCategory.preference,
      source: MemorySource.staffNote,
      isExplicit: true,
      confidence: 0.88,
    ),
    (
      content: 'Bought the emerald raw silk for her sister\'s wedding.',
      category: MemoryCategory.fact,
      source: MemorySource.purchase,
      isExplicit: true,
      confidence: 0.95,
    ),
    (
      content: 'Said the boutique is her first stop for festive shopping.',
      category: MemoryCategory.sentiment,
      source: MemorySource.conversation,
      isExplicit: true,
      confidence: 0.71,
    ),
    (
      content: 'Mentioned the last alteration took three days.',
      category: MemoryCategory.complaint,
      source: MemorySource.conversation,
      isExplicit: false,
      confidence: 0.64,
    ),
    (
      content: 'Has a wedding coming up and wants a reception saree.',
      category: MemoryCategory.event,
      source: MemorySource.conversation,
      isExplicit: false,
      confidence: 0.58,
    ),
    (
      content: 'Usually shops with her daughter, who has opinions.',
      category: MemoryCategory.fact,
      source: MemorySource.inferred,
      isExplicit: false,
      confidence: 0.52,
    ),
  ];

  /// The channels and directions the agent service records.
  static const List<({
    InteractionChannel channel,
    InteractionDirection direction,
    String message,
  })>
  _exchanges = [
    (
      channel: InteractionChannel.whatsapp,
      direction: InteractionDirection.inbound,
      message: 'Is the wine raw silk back in stock yet?',
    ),
    (
      channel: InteractionChannel.whatsapp,
      direction: InteractionDirection.outbound,
      message: 'Held the ivory organza until Friday, as asked.',
    ),
    (
      channel: InteractionChannel.inPerson,
      direction: InteractionDirection.inbound,
      message: 'Came in for a fitting and took the measurements home.',
    ),
    (
      channel: InteractionChannel.phone,
      direction: InteractionDirection.outbound,
      message: 'Rang to confirm the alteration is ready.',
    ),
    (
      channel: InteractionChannel.instagram,
      direction: InteractionDirection.inbound,
      message: 'Sent a photo of the saree she wants matched.',
    ),
  ];

  /// One demo client, with their status derived the way the API derives it.
  Customer _client(
    String id, {
    required String phone,
    required CustomerLevel level,
    String? name,
    String? nickname,
    double spent = 0,
    int visits = 0,
    int daysSinceVisit = 0,
    Set<String> tags = const <String>{},
  }) {
    final lastVisit = _reference.subtract(Duration(days: daysSinceVisit));
    return Customer(
      id: id,
      organizationId: 'org-demo',
      phoneNumber: phone,
      level: level,
      // `CustomerLoyaltyService.RecommendStatus`, at this book's own clock, so a
      // status the demo shows is a status the API would agree with.
      status: CustomerStatus.recommend(
        totalSpent: spent,
        visitCount: visits,
        lastVisitAtUtc: lastVisit,
        now: _reference,
      ),
      fullName: name,
      nickname: nickname,
      email: '${id.toLowerCase()}@example.com',
      totalSpent: spent,
      visitCount: visits,
      lastVisitAtUtc: lastVisit,
      createdAtUtc: _reference.subtract(Duration(days: 180 + visits * 21)),
      tags: tags,
    );
  }

  List<Customer> _buildBook() => <Customer>[
    _client(
      'CUS-1001',
      name: 'Anjali Perera',
      phone: '+94 71 445 2091',
      level: CustomerLevel.level3,
      spent: 184000,
      visits: 9,
      daysSinceVisit: 4,
      tags: const {'bridal'},
    ),
    _client(
      'CUS-1002',
      name: 'Arjun Fernando',
      phone: '+94 77 902 3318',
      level: CustomerLevel.level1,
      spent: 6400,
      visits: 1,
      daysSinceVisit: 12,
    ),
    _client(
      'CUS-1003',
      name: 'Bianca Costa',
      nickname: 'Bibi',
      phone: '+94 76 118 7742',
      level: CustomerLevel.level2,
      spent: 42000,
      visits: 4,
      daysSinceVisit: 31,
      tags: const {'evening'},
    ),
    _client(
      'CUS-1004',
      name: 'Bimal Jayasuriya',
      phone: '+94 70 553 6620',
      level: CustomerLevel.level1,
      spent: 9500,
      visits: 2,
      daysSinceVisit: 68,
    ),
    _client(
      'CUS-1005',
      name: 'Chamari Silva',
      phone: '+94 77 641 2098',
      level: CustomerLevel.vip,
      spent: 512000,
      visits: 14,
      daysSinceVisit: 2,
      tags: const {'bridal', 'atelier-pick'},
    ),
    _client(
      'CUS-1006',
      name: 'Cassie Wijesuriya',
      phone: '+94 71 330 5517',
      level: CustomerLevel.level2,
      spent: 61000,
      visits: 5,
      daysSinceVisit: 21,
    ),
    _client(
      'CUS-1007',
      phone: '+94 77 214 8890',
      level: CustomerLevel.level1,
      spent: 3200,
      visits: 1,
      daysSinceVisit: 240,
    ),
    _client(
      'CUS-1008',
      nickname: 'Chooti',
      phone: '+94 76 887 1123',
      level: CustomerLevel.level2,
      spent: 37500,
      visits: 6,
      daysSinceVisit: 9,
      tags: const {'festive'},
    ),
    _client(
      'CUS-1009',
      name: 'Devaka Mendis',
      phone: '+94 75 209 4471',
      level: CustomerLevel.level1,
      spent: 12800,
      visits: 2,
      daysSinceVisit: 55,
    ),
    _client(
      'CUS-1010',
      name: 'Dilani Ratnayake',
      phone: '+94 77 553 9902',
      level: CustomerLevel.level3,
      spent: 226000,
      visits: 11,
      daysSinceVisit: 6,
      tags: const {'handloom'},
    ),
    _client(
      'CUS-1011',
      name: 'Eleanor Vane',
      phone: '+94 77 001 2286',
      level: CustomerLevel.vip,
      spent: 640000,
      visits: 18,
      daysSinceVisit: 1,
      tags: const {'atelier-pick', 'bridal'},
    ),
    _client(
      'CUS-1012',
      name: 'Eranda Gunasekara',
      phone: '+94 70 884 3390',
      level: CustomerLevel.level1,
      spent: 7400,
      visits: 1,
      daysSinceVisit: 130,
    ),
    _client(
      'CUS-1013',
      name: 'Fathima Rizwan',
      phone: '+94 77 330 6612',
      level: CustomerLevel.level2,
      spent: 52000,
      visits: 5,
      daysSinceVisit: 17,
    ),
    _client(
      'CUS-1014',
      name: 'Gayathri Nair',
      nickname: 'Gaya',
      phone: '+94 76 445 8823',
      level: CustomerLevel.level3,
      spent: 158000,
      visits: 8,
      daysSinceVisit: 13,
      tags: const {'raw-silk'},
    ),
    _client(
      'CUS-1015',
      name: 'Hiruni Bandara',
      phone: '+94 71 992 4471',
      level: CustomerLevel.level3,
      spent: 143000,
      visits: 7,
      daysSinceVisit: 3,
    ),
    _client(
      'CUS-1016',
      name: 'Isabella Ranatunga',
      phone: '+94 77 118 2265',
      level: CustomerLevel.level3,
      spent: 197000,
      visits: 10,
      daysSinceVisit: 5,
      tags: const {'bridal'},
    ),
    _client(
      'CUS-1017',
      name: 'Janith Weerasinghe',
      phone: '+94 75 662 1109',
      level: CustomerLevel.level1,
      spent: 11000,
      visits: 2,
      daysSinceVisit: 96,
    ),
    _client(
      'CUS-1018',
      name: 'Kavindya Alwis',
      phone: '+94 70 221 7788',
      level: CustomerLevel.level2,
      spent: 47500,
      visits: 4,
      daysSinceVisit: 26,
      tags: const {'new-in'},
    ),
    _client(
      'CUS-1019',
      name: 'Lakmini Dias',
      phone: '+94 77 774 3391',
      level: CustomerLevel.level1,
      spent: 15600,
      visits: 3,
      daysSinceVisit: 44,
    ),
    _client(
      'CUS-1020',
      name: 'Mahesh Kodikara',
      phone: '+94 76 553 8820',
      level: CustomerLevel.level1,
      spent: 5900,
      visits: 1,
      daysSinceVisit: 210,
    ),
    _client(
      'CUS-1021',
      name: 'Maya Tennakoon',
      phone: '+94 71 118 6654',
      level: CustomerLevel.level2,
      spent: 88000,
      visits: 6,
      daysSinceVisit: 8,
      tags: const {'bestsellers'},
    ),
    _client(
      'CUS-1022',
      name: 'Nadia Rahman',
      phone: '+94 77 445 9917',
      level: CustomerLevel.level2,
      spent: 73500,
      visits: 6,
      daysSinceVisit: 11,
    ),
    _client(
      'CUS-1023',
      name: 'Nimali Fonseka',
      phone: '+94 75 990 2214',
      level: CustomerLevel.level3,
      spent: 168000,
      visits: 9,
      daysSinceVisit: 19,
      tags: const {'evening'},
    ),
    _client(
      'CUS-1024',
      name: 'Osanda Herath',
      phone: '+94 70 662 5530',
      level: CustomerLevel.level1,
      spent: 8300,
      visits: 2,
      daysSinceVisit: 37,
    ),
    _client(
      'CUS-1025',
      name: 'Priyanka Jayalath',
      phone: '+94 76 221 4478',
      level: CustomerLevel.level2,
      spent: 39000,
      visits: 3,
      daysSinceVisit: 52,
    ),
    _client(
      'CUS-1026',
      name: 'Renuka Balasuriya',
      phone: '+94 77 884 1126',
      level: CustomerLevel.level3,
      spent: 212000,
      visits: 12,
      daysSinceVisit: 7,
      tags: const {'festive'},
    ),
    _client(
      'CUS-1027',
      name: 'Ruwan Senanayake',
      phone: '+94 71 553 6691',
      level: CustomerLevel.level1,
      spent: 13500,
      visits: 2,
      daysSinceVisit: 74,
    ),
    _client(
      'CUS-1028',
      name: 'Sanjaya Ekanayake',
      phone: '+94 75 445 8826',
      level: CustomerLevel.level2,
      spent: 44500,
      visits: 4,
      daysSinceVisit: 29,
    ),
    _client(
      'CUS-1029',
      name: 'Shalini Perera',
      phone: '+94 70 990 3347',
      level: CustomerLevel.level1,
      spent: 9800,
      visits: 2,
      daysSinceVisit: 61,
    ),
    _client(
      'CUS-1030',
      nickname: 'Shan',
      phone: '+94 76 662 1148',
      level: CustomerLevel.level2,
      spent: 56000,
      visits: 5,
      daysSinceVisit: 15,
      tags: const {'under-25k'},
    ),
    _client(
      'CUS-1031',
      name: 'Sophia Liyanage',
      phone: '+94 77 221 7743',
      level: CustomerLevel.level1,
      spent: 8400,
      visits: 1,
      daysSinceVisit: 23,
    ),
    _client(
      'CUS-1032',
      name: 'Thilini Abeywardena',
      phone: '+94 71 774 2290',
      level: CustomerLevel.level3,
      spent: 152000,
      visits: 8,
      daysSinceVisit: 10,
      tags: const {'handloom'},
    ),
    _client(
      'CUS-1033',
      name: 'Upeksha Madushani',
      phone: '+94 75 118 9932',
      level: CustomerLevel.level2,
      spent: 51000,
      visits: 5,
      daysSinceVisit: 42,
    ),
    _client(
      'CUS-1034',
      name: 'Vishwa Rajapaksa',
      phone: '+94 70 445 6618',
      level: CustomerLevel.level1,
      spent: 12200,
      visits: 2,
      daysSinceVisit: 88,
    ),
    _client(
      'CUS-1035',
      name: 'Wasana Kumarasinghe',
      phone: '+94 76 990 4475',
      level: CustomerLevel.level2,
      spent: 41000,
      visits: 3,
      daysSinceVisit: 33,
    ),
    _client(
      'CUS-1036',
      name: 'Yasara Perera',
      phone: '+94 77 662 8891',
      level: CustomerLevel.level3,
      spent: 176000,
      visits: 9,
      daysSinceVisit: 14,
    ),
    _client(
      'CUS-1037',
      name: 'Zahra Nazeer',
      phone: '+94 71 221 5534',
      level: CustomerLevel.level1,
      spent: 6800,
      visits: 1,
      daysSinceVisit: 265,
    ),
    _client(
      'CUS-1038',
      phone: '+94 75 774 1120',
      level: CustomerLevel.level1,
      spent: 4500,
      visits: 1,
      daysSinceVisit: 155,
    ),
  ];
}
