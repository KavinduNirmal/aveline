import 'package:flutter/material.dart';

import '../../../../shared/widgets/section_placeholder.dart';

/// Conversations screen for staff communication and concierge logs.
/// Placeholder until the conversations backend feature slice is connected.
class ConversationsScreen extends StatelessWidget {
  const ConversationsScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return const SectionPlaceholder(
      title: 'Conversations',
      description: 'Client concierge chats, inquiries & team communications',
      icon: Icons.chat_outlined,
    );
  }
}
