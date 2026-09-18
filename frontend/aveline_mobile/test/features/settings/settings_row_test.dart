import 'package:aveline_mobile/core/theme/app_theme.dart';
import 'package:aveline_mobile/features/settings/presentation/widgets/settings_row.dart';
import 'package:aveline_mobile/features/settings/presentation/widgets/settings_switch_row.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

Widget _wrap(Widget child) {
  return MaterialApp(
    theme: AppTheme.light,
    home: Scaffold(body: ListView(children: [child])),
  );
}

void main() {
  group('SettingsRow', () {
    testWidgets('reads its label and the value beside it', (tester) async {
      await tester.pumpWidget(
        _wrap(
          SettingsRow(
            key: const Key('row'),
            icon: Icons.phone_outlined,
            label: 'Phone number',
            value: '+94771234567',
            onTap: () {},
          ),
        ),
      );

      expect(find.text('Phone number'), findsOneWidget);
      expect(find.text('+94771234567'), findsOneWidget);
    });

    testWidgets('marks where the tap leads with a chevron', (tester) async {
      await tester.pumpWidget(
        _wrap(
          SettingsRow(
            key: const Key('row'),
            icon: Icons.storefront_outlined,
            label: 'Boutique',
            value: 'Ceylon Atelier',
            onTap: () {},
          ),
        ),
      );

      expect(find.byIcon(Icons.chevron_right_rounded), findsOneWidget);
    });

    testWidgets('leaves an inert row without a chevron', (tester) async {
      await tester.pumpWidget(
        _wrap(
          SettingsRow(
            key: const Key('row'),
            icon: Icons.storefront_outlined,
            label: 'Boutique',
            value: 'Ceylon Atelier',
          ),
        ),
      );

      // A chevron promises somewhere to go. Drawing one on a row that goes
      // nowhere is the row lying about itself.
      expect(find.byIcon(Icons.chevron_right_rounded), findsNothing);
    });

    testWidgets('reports a tap', (tester) async {
      var taps = 0;
      await tester.pumpWidget(
        _wrap(
          SettingsRow(
            key: const Key('row'),
            icon: Icons.edit_outlined,
            label: 'Edit details',
            onTap: () => taps++,
          ),
        ),
      );

      await tester.tap(find.byKey(const Key('row')));
      await tester.pump();

      expect(taps, 1);
    });

    testWidgets('paints a destructive row in the error colour', (tester) async {
      await tester.pumpWidget(
        _wrap(
          SettingsRow(
            key: const Key('row'),
            icon: Icons.logout_rounded,
            label: 'Sign out',
            destructive: true,
            onTap: () {},
          ),
        ),
      );

      final label = tester.widget<Text>(find.text('Sign out'));
      expect(label.style?.color, AppTheme.light.colorScheme.error);
    });
  });

  group('SettingsSwitchRow', () {
    testWidgets('reads its state off the value it is handed', (tester) async {
      await tester.pumpWidget(
        _wrap(
          SettingsSwitchRow(
            key: const Key('row'),
            icon: Icons.notifications_active_outlined,
            label: 'Push notifications',
            description: 'Client messages and approvals.',
            value: true,
            onChanged: (_) {},
          ),
        ),
      );

      expect(tester.widget<Switch>(find.byType(Switch)).value, isTrue);
      expect(find.text('Push notifications'), findsOneWidget);
      expect(find.text('Client messages and approvals.'), findsOneWidget);
    });

    testWidgets('reports the opposite value when it is switched', (tester) async {
      bool? reported;
      await tester.pumpWidget(
        _wrap(
          SettingsSwitchRow(
            key: const Key('row'),
            icon: Icons.notifications_active_outlined,
            label: 'Push notifications',
            value: true,
            onChanged: (value) => reported = value,
          ),
        ),
      );

      await tester.tap(find.byType(Switch));
      await tester.pump();

      expect(reported, isFalse);
    });

    testWidgets('refuses the switch while its save is in flight', (tester) async {
      await tester.pumpWidget(
        _wrap(
          const SettingsSwitchRow(
            key: Key('row'),
            icon: Icons.notifications_active_outlined,
            label: 'Push notifications',
            value: true,
            onChanged: null,
          ),
        ),
      );

      expect(tester.widget<Switch>(find.byType(Switch)).onChanged, isNull);
    });
  });
}
