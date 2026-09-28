import 'package:flutter/material.dart';

import '../../../../shared/widgets/section_placeholder.dart';

/// Notifications stub screen: minimal placeholder destination for the header notification icon.
class NotificationsStubScreen extends StatelessWidget {
  const NotificationsStubScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return const SectionPlaceholder(
      title: 'Notifications',
      description: 'System alerts, client updates, and operational notices',
      icon: Icons.notifications_outlined,
    );
  }
}
