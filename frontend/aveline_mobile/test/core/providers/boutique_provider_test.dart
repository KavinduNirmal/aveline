import 'dart:convert';
import 'dart:typed_data';

import 'package:aveline_mobile/core/providers/boutique_provider.dart';
import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';

/// Serves `GET /orgs/my` from a canned body, or refuses the connection.
class _StubAdapter implements HttpClientAdapter {
  _StubAdapter(this.body);

  /// The membership list the endpoint returns.
  Object? body;
  bool fail = false;
  int statusCode = 200;
  String? lastPath;

  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) async {
    lastPath = options.path;
    if (fail) {
      throw DioException.connectionError(
        requestOptions: options,
        reason: 'Connection refused',
      );
    }
    if (statusCode != 200) {
      return ResponseBody.fromString('{}', statusCode);
    }
    return ResponseBody.fromString(
      jsonEncode(body),
      200,
      headers: {
        Headers.contentTypeHeader: [Headers.jsonContentType],
      },
    );
  }

  @override
  void close({bool force = false}) {}
}

void main() {
  late _StubAdapter adapter;
  late Dio dio;
  late BoutiqueProvider provider;

  setUp(() {
    adapter = _StubAdapter(const []);
    dio = Dio()..httpClientAdapter = adapter;
    provider = BoutiqueProvider();
  });

  group('BoutiqueProvider.fetchBoutique', () {
    test('reads the active membership from /orgs/my', () async {
      adapter.body = [
        {'organizationName': 'Old Atelier', 'status': 'Invited'},
        {'organizationName': 'Ceylon Atelier', 'status': 'Active'},
      ];

      await provider.fetchBoutique(dio);

      expect(provider.name, 'Ceylon Atelier');
      expect(provider.hasLoadFailed, isFalse);
      expect(adapter.lastPath, '/api/v1/orgs/my');
    });

    test('keeps the active membership id and role beside the name', () async {
      adapter.body = [
        {
          'organizationId': '11111111-1111-1111-1111-111111111111',
          'organizationName': 'Old Atelier',
          'boutiqueRole': 'org:boutique_staff',
          'status': 'Invited',
        },
        {
          'organizationId': '22222222-2222-2222-2222-222222222222',
          'organizationName': 'Ceylon Atelier',
          'boutiqueRole': 'org:boutique_manager',
          'status': 'Active',
        },
      ];

      await provider.fetchBoutique(dio);

      // The id, the role and the name must describe one membership, so a screen
      // can build `/orgs/{id}/...` for the shop whose name it is showing.
      expect(provider.name, 'Ceylon Atelier');
      expect(provider.organizationId, '22222222-2222-2222-2222-222222222222');
      expect(provider.boutiqueRole, 'org:boutique_manager');
    });

    test('takes the id and role from the membership that supplied the name',
        () async {
      adapter.body = [
        {
          'organizationId': '33333333-3333-3333-3333-333333333333',
          'organizationName': '  Ceylon Atelier  ',
          'boutiqueRole': 'org:boutique_owner',
          'status': 'Invited',
        },
        {
          'organizationId': '44444444-4444-4444-4444-444444444444',
          'organizationName': 'Second Atelier',
          'boutiqueRole': 'org:boutique_staff',
          'status': 'Suspended',
        },
      ];

      await provider.fetchBoutique(dio);

      expect(provider.name, 'Ceylon Atelier');
      expect(provider.organizationId, '33333333-3333-3333-3333-333333333333');
      expect(provider.boutiqueRole, 'org:boutique_owner');
    });

    test(
      'falls back to the first named membership when none is active',
      () async {
        adapter.body = [
          {'organizationName': '  Ceylon Atelier  ', 'status': 'Invited'},
          {'organizationName': 'Second Atelier', 'status': 'Suspended'},
        ];

        await provider.fetchBoutique(dio);

        // Trimmed, and the unnamed-but-present memberships are still skipped.
        expect(provider.name, 'Ceylon Atelier');
      },
    );

    test('skips memberships with no boutique name', () async {
      adapter.body = [
        {'organizationName': '   ', 'status': 'Active'},
        {'organizationName': null, 'status': 'Active'},
        {'organizationName': 'Ceylon Atelier', 'status': 'Active'},
      ];

      await provider.fetchBoutique(dio);

      expect(provider.name, 'Ceylon Atelier');
    });

    test('reports a refused connection as a load failure', () async {
      adapter.fail = true;

      await provider.fetchBoutique(dio);

      expect(provider.hasLoadFailed, isTrue);
      expect(provider.name, isNull);
      expect(provider.errorMessage, contains('Connection refused'));
    });

    test('reports a non-200 response as a load failure', () async {
      adapter.statusCode = 403;

      await provider.fetchBoutique(dio);

      expect(provider.hasLoadFailed, isTrue);
      expect(provider.name, isNull);
    });

    test('reports an empty membership list as no boutique', () async {
      adapter.body = const [];

      await provider.fetchBoutique(dio);

      expect(provider.name, isNull);
      // No membership means no org id, which is a hard stop for org-scoped calls.
      expect(provider.organizationId, isNull);
      expect(provider.boutiqueRole, isNull);
      expect(provider.hasLoadFailed, isTrue);
    });

    test('clears the loaded name, role and org id', () async {
      adapter.body = [
        {
          'organizationId': '22222222-2222-2222-2222-222222222222',
          'organizationName': 'Ceylon Atelier',
          'boutiqueRole': 'org:boutique_manager',
          'status': 'Active',
        },
      ];
      await provider.fetchBoutique(dio);
      expect(provider.name, 'Ceylon Atelier');

      provider.clear();

      expect(provider.name, isNull);
      expect(provider.organizationId, isNull);
      expect(provider.boutiqueRole, isNull);
      expect(provider.hasLoadFailed, isFalse);
    });
  });
}
