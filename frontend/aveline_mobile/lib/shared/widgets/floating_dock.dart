import 'package:flutter/material.dart';

import 'blossom.dart';

/// The dock destinations that are not the center Salon launcher.
enum DockTab { home, customers, catalog, profile }

/// Aveline's floating bottom dock, mirroring the web shell's navigation in a
/// mobile-first form. Four line-icon tabs flank a raised center launcher that
/// carries the Blossom mark and opens the full-screen Salon.
///
/// The dock is rendered as a floating pill with a soft ambient shadow and a
/// frosted surface, per the "glassmorphism used sparingly for floating
/// navigation" guidance in `.agents/brain/DESIGN.md`.
class FloatingDock extends StatelessWidget {
  const FloatingDock({
    super.key,
    required this.current,
    required this.onSelect,
    required this.onOpenSalon,
  });

  /// The currently active [DockTab].
  final DockTab current;

  /// Called when a non-center tab is tapped.
  final ValueChanged<DockTab> onSelect;

  /// Called when the center Salon launcher is tapped.
  final VoidCallback onOpenSalon;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;

    return SafeArea(
      top: false,
      child: Padding(
        padding: const EdgeInsets.fromLTRB(20, 0, 20, 16),
        child: Container(
          height: 68,
          padding: const EdgeInsets.symmetric(horizontal: 8),
          decoration: BoxDecoration(
            color: scheme.surfaceContainerLowest.withValues(alpha: 0.92),
            borderRadius: BorderRadius.circular(34),
            border: Border.all(color: scheme.outlineVariant.withValues(alpha: 0.6)),
            boxShadow: [
              BoxShadow(
                color: const Color(0xFF8B2E42).withValues(alpha: 0.10),
                blurRadius: 24,
                offset: const Offset(0, 8),
              ),
            ],
          ),
          child: Row(
            mainAxisAlignment: MainAxisAlignment.spaceAround,
            children: [
              _DockItem(
                icon: Icons.home_outlined,
                activeIcon: Icons.home_rounded,
                label: 'Home',
                selected: current == DockTab.home,
                onTap: () => onSelect(DockTab.home),
              ),
              _DockItem(
                icon: Icons.people_outline,
                activeIcon: Icons.people_rounded,
                label: 'Customers',
                selected: current == DockTab.customers,
                onTap: () => onSelect(DockTab.customers),
              ),
              _SalonLauncher(onTap: onOpenSalon),
              _DockItem(
                icon: Icons.checkroom_outlined,
                activeIcon: Icons.checkroom_rounded,
                label: 'Catalog',
                selected: current == DockTab.catalog,
                onTap: () => onSelect(DockTab.catalog),
              ),
              _DockItem(
                icon: Icons.person_outline,
                activeIcon: Icons.person_rounded,
                label: 'Profile',
                selected: current == DockTab.profile,
                onTap: () => onSelect(DockTab.profile),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// A single line-icon dock item with an active (filled) state.
class _DockItem extends StatelessWidget {
  const _DockItem({
    required this.icon,
    required this.activeIcon,
    required this.label,
    required this.selected,
    required this.onTap,
  });

  final IconData icon;
  final IconData activeIcon;
  final String label;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final color = selected ? scheme.primary : scheme.onSurfaceVariant;

    return Semantics(
      button: true,
      selected: selected,
      label: label,
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(20),
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 6),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(selected ? activeIcon : icon, color: color, size: 24),
              const SizedBox(height: 2),
              Text(
                label,
                style: Theme.of(context).textTheme.labelSmall?.copyWith(
                      color: color,
                      fontSize: 10,
                    ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// The raised center launcher: a circular primary-toned button carrying the
/// Blossom mark. Opens the full-screen Salon.
class _SalonLauncher extends StatelessWidget {
  const _SalonLauncher({required this.onTap});

  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;

    return Semantics(
      button: true,
      label: 'Open Salon',
      child: GestureDetector(
        onTap: onTap,
        child: Container(
          width: 56,
          height: 56,
          margin: const EdgeInsets.only(top: 0),
          decoration: BoxDecoration(
            shape: BoxShape.circle,
            gradient: LinearGradient(
              begin: Alignment.topLeft,
              end: Alignment.bottomRight,
              colors: [scheme.primary, const Color(0xFFC05267)],
            ),
            boxShadow: [
              BoxShadow(
                color: scheme.primary.withValues(alpha: 0.35),
                blurRadius: 16,
                offset: const Offset(0, 6),
              ),
            ],
          ),
          child: const Blossom(size: 30, color: Colors.white),
        ),
      ),
    );
  }
}
