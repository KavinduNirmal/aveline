import 'package:flutter/material.dart';

import '../../../../shared/widgets/floating_dock.dart';
import '../../../catalog/presentation/screens/catalog_screen.dart';
import '../../../customers/presentation/screens/customers_screen.dart';
import '../../../profile/presentation/screens/profile_screen.dart';
import '../../../salon/presentation/screens/salon_screen.dart';
import 'home_screen.dart';

/// Post-auth landing shell: hosts the floating dock and swaps the active tab
/// body beneath it. The center Salon launcher navigates to the full-screen
/// Salon route (the dock is hidden there).
class MainShell extends StatefulWidget {
  const MainShell({super.key});

  @override
  State<MainShell> createState() => _MainShellState();
}

class _MainShellState extends State<MainShell> {
  DockTab _current = DockTab.home;

  void _select(DockTab tab) {
    if (tab == _current) return;
    setState(() => _current = tab);
  }

  @override
  Widget build(BuildContext context) {
    final body = switch (_current) {
      DockTab.home => const HomeScreen(),
      DockTab.customers => const CustomersScreen(),
      DockTab.catalog => const CatalogScreen(),
      DockTab.profile => const ProfileScreen(),
    };

    return Scaffold(
      body: Stack(
        children: [
          Positioned.fill(child: body),
          Align(
            alignment: Alignment.bottomCenter,
            child: FloatingDock(
              current: _current,
              onSelect: _select,
              onOpenSalon: () => Navigator.of(context).push(
                MaterialPageRoute<void>(
                  builder: (_) => const SalonScreen(),
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}
