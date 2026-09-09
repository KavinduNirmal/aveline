import 'package:flutter/material.dart';

import '../../../../shared/widgets/section_placeholder.dart';

/// Customers dock tab. Placeholder until the customer concierge & memory slice
/// is built; mirrors the web Customers section.
class CustomersScreen extends StatelessWidget {
  const CustomersScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return const SectionPlaceholder(
      title: 'Customers',
      description: 'Customer concierge & memory',
      icon: Icons.people_outline,
    );
  }
}
