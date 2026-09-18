import 'dart:convert';
import 'dart:typed_data';

import 'package:aveline_mobile/core/auth/app_roles.dart';
import 'package:aveline_mobile/core/providers/boutique_provider.dart';
import 'package:aveline_mobile/core/providers/user_provider.dart';
import 'package:aveline_mobile/core/theme/app_theme.dart';
import 'package:aveline_mobile/features/auth/domain/aveline_user.dart';
import 'package:aveline_mobile/features/settings/presentation/screens/settings_screen.dart';
import 'package:aveline_mobile/features/settings/presentation/widgets/settings_row.dart';
import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:provider/provider.dart';

/// What `GET /api/v1/users/me` answers, kept in the shape the API sends.
Map<String, dynamic> _profileJson({
  String userRole = AppRoles.staff,
  String organizationRole = AppRoles.boutiqueStaff,
  String? displayName = 'Charlotte Tilbury',
  String? phoneNumber,
  String contactPreference = 'WhatsApp',
  bool pushNotificationsEnabled = true,
  String? profileImageUrl,
}) => {
  'id': 'usr_1',
  'clerkId': 'clerk_1',
  'email': 'charlotte@aveline.com',
  'firstName': 'Charlotte',
  'lastName': 'Tilbury',
  'displayName': displayName,
  'username': 'charlotte',
  'phoneNumber': phoneNumber,
  'profileImageUrl': profileImageUrl,
  'userRole': userRole,
  'organizationRole': organizationRole,
  'organizationId': 'org_1',
  'hasCompletedOnboarding': true,
  'accountState': 'Active',
  'contactPreference': contactPreference,
  'pushNotificationsEnabled': pushNotificationsEnabled,
  'isActive': true,
};

/// Answers the two calls this screen makes, and records what it was handed.
///
/// `PATCH` answers with the record the API would hold afterwards, which is what
/// lets the screen's controls follow the server rather than their own guess.
class _SettingsAdapter implements HttpClientAdapter {
  _SettingsAdapter({Map<String, dynamic>? profile})
    : profile = profile ?? _profileJson();

  Map<String, dynamic> profile;

  bool failPatch = false;
  int patchStatusCode = 200;
  String patchMessage = 'That change was refused.';
  int patchCalls = 0;
  String? lastMethod;
  String? lastPath;
  Map<String, dynamic>? lastBody;

  /// What `GET /api/v1/orgs/my` answers with.
  List<Map<String, dynamic>> memberships = const [];

  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) async {
    final headers = {Headers.contentTypeHeader: [Headers.jsonContentType]};

    if (options.uri.path.endsWith('/orgs/my')) {
      return ResponseBody.fromString(jsonEncode(memberships), 200, headers: headers);
    }

    lastMethod = options.method;
    lastPath = options.uri.path;
    final sent = options.data;
    final body = sent is Map
        ? sent.map((key, value) => MapEntry(key.toString(), value))
        : null;
    lastBody = body;

    if (options.method == 'PATCH') {
      patchCalls++;
      if (failPatch) {
        throw DioException.connectionError(
          requestOptions: options,
          reason: 'Connection refused',
        );
      }
      if (patchStatusCode != 200) {
        return ResponseBody.fromString(
          jsonEncode(<String, dynamic>{'message': patchMessage}),
          patchStatusCode,
          headers: headers,
        );
      }
      // The API changes only the fields it was handed and answers the whole
      // record, so the new profile is the old one with the change laid over it.
      profile = <String, dynamic>{...profile, ...?body};
    }

    return ResponseBody.fromString(jsonEncode(profile), 200, headers: headers);
  }

  @override
  void close({bool force = false}) {}
}

/// A phone-shaped surface.
///
/// The default test surface is 800x600, which is neither a phone nor tall enough
/// for a settings page under the test font, where every glyph is a square.
void _usePhoneSurface(WidgetTester tester, {double height = 2532}) {
  tester.view.physicalSize = Size(1170, height);
  tester.view.devicePixelRatio = 3.0;
  addTearDown(tester.view.reset);
}

/// Mounts the screen the way the shell does: a tab body inside a `Scaffold`, with
/// the account the app would be holding above it.
Future<UserProvider> _open(
  WidgetTester tester, {
  Map<String, dynamic>? profile,
  _SettingsAdapter? adapter,
  BoutiqueProvider? boutique,
  VoidCallback? onSignOut,
  String? boutiqueName = 'Ceylon Atelier',
  double surfaceHeight = 2532,
}) async {
  _usePhoneSurface(tester, height: surfaceHeight);
  final userProvider = UserProvider()..setUser(AvelineUser.fromJson(profile ?? _profileJson()));
  final dio = Dio()..httpClientAdapter = adapter ?? _SettingsAdapter(profile: profile);

  await tester.pumpWidget(
    MultiProvider(
      providers: [
        ChangeNotifierProvider<UserProvider>.value(value: userProvider),
        if (boutique != null)
          ChangeNotifierProvider<BoutiqueProvider>.value(value: boutique),
        Provider<Dio>.value(value: dio),
      ],
      child: MaterialApp(
        theme: AppTheme.light,
        home: Builder(
          builder: (context) => MediaQuery(
            data: MediaQuery.of(context).copyWith(disableAnimations: true),
            child: Scaffold(
              body: SettingsScreen(
                boutiqueName: boutiqueName,
                onSignOut: onSignOut,
              ),
            ),
          ),
        ),
      ),
    ),
  );
  await tester.pumpAndSettle();
  return userProvider;
}

/// Brings a row below the fold into view, then lets it lay out.
Future<void> _scrollTo(WidgetTester tester, Key key) async {
  await tester.scrollUntilVisible(
    find.byKey(key),
    240,
    scrollable: find.byType(Scrollable).first,
  );
  await tester.pumpAndSettle();
}

/// Lets a toast's own timer run out, so the test does not end with it pending.
Future<void> _letToastExpire(WidgetTester tester) async {
  await tester.pump(const Duration(seconds: 4));
  await tester.pumpAndSettle();
}

Switch _pushSwitch(WidgetTester tester) => tester.widget<Switch>(
  find.descendant(
    of: find.byKey(const Key('settings_push_notifications')),
    matching: find.byType(Switch),
  ),
);

/// The value a settings row is reading, so a test never has to guess which
/// occurrence of a string on the page is the one it means.
String? _rowValue(WidgetTester tester, String key) =>
    tester.widget<SettingsRow>(find.byKey(Key(key))).value;

void main() {
  group('SettingsScreen header and account', () {
    testWidgets('titles itself with the boutique and the section', (tester) async {
      await _open(tester);

      expect(find.byKey(const Key('settings_title')), findsOneWidget);
      expect(find.text('Ceylon Atelier - Settings'), findsOneWidget);
    });

    testWidgets('merges the retired profile screen into an account card', (tester) async {
      await _open(tester);

      expect(find.byKey(const Key('settings_account_card')), findsOneWidget);
      expect(find.text('Charlotte Tilbury'), findsOneWidget);
      expect(find.text('charlotte@aveline.com'), findsOneWidget);
      // Named, not pasted: the claims carry `staff` and `org:boutique_staff`, and
      // neither is something an associate should have to read about themselves.
      expect(find.text('Team role: ${AppRoles.labelFor(AppRoles.staff)}'), findsOneWidget);
      expect(
        find.text('Store role: ${AppRoles.labelFor(AppRoles.boutiqueStaff)}'),
        findsOneWidget,
      );
    });

    testWidgets('carries the account section before the preferences', (tester) async {
      // A surface tall enough for both sections to be built at once: the
      // assertion is about where they sit in the page, not about scrolling.
      await _open(tester, surfaceHeight: 9000);

      final account = tester.getTopLeft(
        find.byKey(const Key('settings_section_account')),
      );
      final notifications = tester.getTopLeft(
        find.byKey(const Key('settings_section_notifications')),
      );

      expect(account.dy, lessThan(notifications.dy));
    });

    testWidgets('shows the number on file when one is set', (tester) async {
      await _open(tester, profile: _profileJson(phoneNumber: '+94771234567'));

      expect(find.byKey(const Key('settings_phone')), findsOneWidget);
      expect(find.text('+94771234567'), findsOneWidget);
    });

    testWidgets('leaves the card to the edit action when no number is on file', (tester) async {
      await _open(tester);

      expect(find.byKey(const Key('settings_phone')), findsNothing);
      expect(find.byKey(const Key('settings_edit_details')), findsOneWidget);
    });

    testWidgets('falls back to the name parts when no display name is set', (tester) async {
      await _open(tester, profile: _profileJson(displayName: null));

      expect(find.text('Charlotte Tilbury'), findsOneWidget);
    });
  });

  group('SettingsScreen notifications and contact', () {
    testWidgets('reads the switch off the record the app holds', (tester) async {
      await _open(
        tester,
        profile: _profileJson(pushNotificationsEnabled: false),
      );

      expect(_pushSwitch(tester).value, isFalse);
    });

    testWidgets('saves a flipped switch and keeps the answer the server gave', (tester) async {
      final adapter = _SettingsAdapter();
      await _open(tester, adapter: adapter);

      await tester.tap(
        find.descendant(
          of: find.byKey(const Key('settings_push_notifications')),
          matching: find.byType(Switch),
        ),
      );
      await tester.pumpAndSettle();

      expect(adapter.lastMethod, 'PATCH');
      expect(adapter.lastPath, '/api/v1/users/me');
      expect(adapter.lastBody, {'pushNotificationsEnabled': false});
      expect(_pushSwitch(tester).value, isFalse);
    });

    testWidgets('reverts a switch whose save was refused, and says why', (tester) async {
      final adapter = _SettingsAdapter()..failPatch = true;
      await _open(tester, adapter: adapter);

      await tester.tap(
        find.descendant(
          of: find.byKey(const Key('settings_push_notifications')),
          matching: find.byType(Switch),
        ),
      );
      await tester.pumpAndSettle();

      // The switch reads the held record, so a save that failed puts it back
      // without the screen having to remember what it used to be.
      expect(_pushSwitch(tester).value, isTrue);
      expect(find.textContaining('Connection refused'), findsOneWidget);
      await _letToastExpire(tester);
    });

    testWidgets('saves the contact preference the associate picks', (tester) async {
      final adapter = _SettingsAdapter();
      await _open(tester, adapter: adapter);

      await _scrollTo(tester, const Key('settings_contact_preference'));
      await tester.tap(find.byKey(const Key('settings_contact_preference')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('contact_preference_option_sms')));
      await tester.pumpAndSettle();

      // The wire value, not the label: `ContactPreferences` reads `SMS`.
      expect(adapter.lastBody, {'contactPreference': 'SMS'});
      // The row reads the label back, because `Preferred contact: Text message`
      // is the sentence the associate asked to see.
      expect(_rowValue(tester, 'settings_contact_preference'), 'Text message');
    });

    testWidgets('leaves the preference alone when the picker is dismissed', (tester) async {
      final adapter = _SettingsAdapter();
      await _open(tester, adapter: adapter);

      await _scrollTo(tester, const Key('settings_contact_preference'));
      await tester.tap(find.byKey(const Key('settings_contact_preference')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('contact_preference_cancel')));
      await tester.pumpAndSettle();

      expect(adapter.patchCalls, 0);
    });

    testWidgets('shows nothing to toggle while the profile is still loading', (tester) async {
      _usePhoneSurface(tester);
      await tester.pumpWidget(
        MultiProvider(
          providers: [
            ChangeNotifierProvider<UserProvider>.value(value: UserProvider()),
            Provider<Dio>.value(value: Dio()..httpClientAdapter = _SettingsAdapter()),
          ],
          child: MaterialApp(
            theme: AppTheme.light,
            home: Builder(
              builder: (context) => MediaQuery(
                data: MediaQuery.of(context).copyWith(disableAnimations: true),
                child: const Scaffold(
                  body: SettingsScreen(boutiqueName: 'Ceylon Atelier'),
                ),
              ),
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();

      // There is no record to read a switch or a preference off, so the screen
      // offers neither rather than a control that would write a guess.
      expect(find.byKey(const Key('settings_push_notifications')), findsNothing);
      expect(find.byKey(const Key('settings_contact_preference')), findsNothing);
      expect(find.byKey(const Key('settings_section_account')), findsOneWidget);
      expect(find.byKey(const Key('settings_account_loading')), findsOneWidget);
    });
  });

  group('SettingsScreen shop block', () {
    testWidgets('hides the shop block from a role that may not manage the shop', (tester) async {
      await _open(tester);

      await _scrollTo(tester, const Key('settings_sign_out'));
      expect(find.byKey(const Key('settings_section_boutique')), findsNothing);
    });

    testWidgets('shows the shop block to a role that may manage it', (tester) async {
      await _open(
        tester,
        profile: _profileJson(
          userRole: AppRoles.staff,
          organizationRole: AppRoles.boutiqueOwner,
        ),
      );

      await _scrollTo(tester, const Key('settings_section_boutique'));

      expect(find.byKey(const Key('settings_section_boutique')), findsOneWidget);
      expect(_rowValue(tester, 'settings_boutique_name'), 'Ceylon Atelier');
      expect(
        _rowValue(tester, 'settings_store_role'),
        AppRoles.labelFor(AppRoles.boutiqueOwner),
      );
    });

    testWidgets('names the boutique the provider loaded when it is not overridden', (tester) async {
      final orgAdapter = _SettingsAdapter()
        ..memberships = [
          {
            'organizationId': 'org_1',
            'organizationName': 'Ceylon Atelier',
            'boutiqueRole': AppRoles.boutiqueOwner,
            'status': 'Active',
          },
        ];
      final dio = Dio()..httpClientAdapter = orgAdapter;
      final boutique = BoutiqueProvider();
      // `runAsync` because this is real async work outside the widget tree: the
      // fake clock the tests pump does not drive Dio's own futures.
      await tester.runAsync(() => boutique.fetchBoutique(dio));

      await _open(
        tester,
        profile: _profileJson(organizationRole: AppRoles.boutiqueOwner),
        boutique: boutique,
        boutiqueName: null,
      );

      // Before scrolling, while the header is still built.
      expect(find.text('Ceylon Atelier - Settings'), findsOneWidget);

      await _scrollTo(tester, const Key('settings_section_boutique'));
      expect(_rowValue(tester, 'settings_boutique_name'), 'Ceylon Atelier');
    });
  });

  group('SettingsScreen session', () {
    testWidgets('signing out runs the action the shell gave it', (tester) async {
      var signedOut = false;
      await _open(tester, onSignOut: () => signedOut = true);

      await _scrollTo(tester, const Key('settings_sign_out'));
      await tester.tap(find.byKey(const Key('settings_sign_out')));
      await tester.pumpAndSettle();

      expect(signedOut, isTrue);
    });
  });

  group('SettingsScreen details sheet', () {
    testWidgets('saves the details the associate typed', (tester) async {
      final adapter = _SettingsAdapter();
      await _open(tester, adapter: adapter);

      await tester.tap(find.byKey(const Key('settings_edit_details')));
      await tester.pumpAndSettle();
      await tester.enterText(
        find.byKey(const Key('edit_profile_display_name')),
        'Charlotte T.',
      );
      await tester.enterText(
        find.byKey(const Key('edit_profile_phone')),
        '+94770000000',
      );
      await tester.tap(find.byKey(const Key('edit_profile_save')));
      await tester.pumpAndSettle();

      expect(adapter.lastBody, {
        'displayName': 'Charlotte T.',
        'phoneNumber': '+94770000000',
      });
      // The card follows the answer the API gave, so the new name is on it.
      expect(find.text('Charlotte T.'), findsOneWidget);
    });

    testWidgets('leaves the record alone when the sheet is cancelled', (tester) async {
      final adapter = _SettingsAdapter();
      await _open(tester, adapter: adapter);

      await tester.tap(find.byKey(const Key('settings_edit_details')));
      await tester.pumpAndSettle();
      await tester.enterText(
        find.byKey(const Key('edit_profile_display_name')),
        'Someone Else',
      );
      await tester.tap(find.byKey(const Key('edit_profile_cancel')));
      await tester.pumpAndSettle();

      expect(adapter.patchCalls, 0);
      expect(find.text('Charlotte Tilbury'), findsOneWidget);
    });

    testWidgets('reports a refused save and keeps the old details', (tester) async {
      final adapter = _SettingsAdapter()
        ..patchStatusCode = 400
        ..patchMessage = 'Display name is too long.';
      await _open(tester, adapter: adapter);

      await tester.tap(find.byKey(const Key('settings_edit_details')));
      await tester.pumpAndSettle();
      await tester.enterText(
        find.byKey(const Key('edit_profile_display_name')),
        'Charlotte Tilbury the Third',
      );
      await tester.tap(find.byKey(const Key('edit_profile_save')));
      await tester.pumpAndSettle();

      // The API's own reason, not Dio's explanation of validateStatus.
      expect(find.text('Display name is too long.'), findsOneWidget);
      expect(find.text('Charlotte Tilbury'), findsOneWidget);
      await _letToastExpire(tester);
    });
  });
}
