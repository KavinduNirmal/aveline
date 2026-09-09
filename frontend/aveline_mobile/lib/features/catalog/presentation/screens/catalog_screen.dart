import 'package:flutter/material.dart';

import '../../../../shared/widgets/section_placeholder.dart';

/// Catalog dock tab. Placeholder until the visual intelligence & sourcing slice
/// is built; mirrors the web Catalog section.
class CatalogScreen extends StatelessWidget {
  const CatalogScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return const SectionPlaceholder(
      title: 'Catalog',
      description: 'Visual intelligence & sourcing',
      icon: Icons.checkroom_outlined,
    );
  }
}
