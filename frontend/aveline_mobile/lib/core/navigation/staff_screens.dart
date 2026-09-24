import 'package:flutter/material.dart';

import '../../features/catalog/presentation/screens/catalog_screen.dart';
import '../../features/conversations/presentation/screens/conversations_screen.dart';
import '../../features/customers/presentation/screens/customers_screen.dart';
import '../../features/home/presentation/screens/home_screen.dart';
import '../../features/notifications/presentation/screens/notifications_screen.dart';
import '../../features/settings/presentation/screens/settings_screen.dart';
import '../auth/permissions.dart';
import '../router/route_guards.dart';
import 'screen_config.dart';

/// Registry of screens available in the Staff application UI shell.
List<ScreenConfig> staffScreens() => [
  ScreenConfig(
    id: 'home',
    label: 'Home',
    icon: Icons.home_outlined,
    activeIcon: Icons.home_rounded,
    route: AppRoutes.home,
    permission: null,
    builder: (_) => const HomeScreen(),
  ),
  ScreenConfig(
    id: 'customers',
    label: 'Customers',
    icon: Icons.people_outline,
    activeIcon: Icons.people_rounded,
    route: AppRoutes.customers,
    permission: Permissions.customersView,
    builder: (_) => const CustomersScreen(),
  ),
  ScreenConfig(
    id: 'catalog',
    label: 'Catalog',
    icon: Icons.checkroom_outlined,
    activeIcon: Icons.checkroom_rounded,
    route: AppRoutes.catalog,
    permission: Permissions.catalogView,
    builder: (_) => const CatalogScreen(),
  ),
  ScreenConfig(
    id: 'orders',
    label: 'Orders',
    icon: Icons.receipt_long_outlined,
    activeIcon: Icons.receipt_long_rounded,
    route: AppRoutes.orders,
    permission: null,
    builder: (_) => const SizedBox.shrink(),
  ),
  ScreenConfig(
    id: 'conversations',
    // The screen titles itself `Messages`, and the two used to disagree: the
    // panel pointed at a word the page it opened never said. Only the label was
    // wrong, so only the label changed.
    label: 'Messages',
    icon: Icons.chat_outlined,
    activeIcon: Icons.chat_rounded,
    route: AppRoutes.conversations,
    permission: Permissions.conversationsView,
    builder: (_) => const ConversationsScreen(),
  ),
  ScreenConfig(
    id: 'notifications',
    label: 'Notifications',
    icon: Icons.notifications_none_rounded,
    activeIcon: Icons.notifications_rounded,
    route: AppRoutes.notifications,
    // The associate's own inbox rather than the shop's, so no grant is needed to
    // reach it.
    permission: null,
    builder: (_) => const NotificationsScreen(),
  ),
  ScreenConfig(
    id: 'settings',
    label: 'Settings',
    icon: Icons.settings_outlined,
    activeIcon: Icons.settings_rounded,
    route: AppRoutes.settings,
    // Deliberately ungated. Settings holds the associate's account and
    // preferences, which is why it absorbed the Profile tab; the shop-wide block
    // inside it is what answers to `settings:manage`.
    permission: null,
    builder: (_) => const SettingsScreen(),
  ),
];
