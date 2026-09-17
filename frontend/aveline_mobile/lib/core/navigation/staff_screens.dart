import 'package:flutter/material.dart';

import '../../features/catalog/presentation/screens/catalog_screen.dart';
import '../../features/conversations/presentation/screens/conversations_screen.dart';
import '../../features/home/presentation/screens/home_screen.dart';
import '../auth/permissions.dart';
import 'screen_config.dart';

/// Registry of screens available in the Staff application UI shell.
List<ScreenConfig> staffScreens() => [
  ScreenConfig(
    id: 'home',
    label: 'Home',
    icon: Icons.home_outlined,
    activeIcon: Icons.home_rounded,
    route: '/',
    permission: null,
    builder: (_) => const HomeScreen(),
  ),
  ScreenConfig(
    id: 'catalog',
    label: 'Catalog',
    icon: Icons.checkroom_outlined,
    activeIcon: Icons.checkroom_rounded,
    route: '/catalog',
    permission: Permissions.catalogView,
    builder: (_) => const CatalogScreen(),
  ),
  ScreenConfig(
    id: 'conversations',
    label: 'Conversations',
    icon: Icons.chat_outlined,
    activeIcon: Icons.chat_rounded,
    route: '/conversations',
    permission: Permissions.conversationsView,
    builder: (_) => const ConversationsScreen(),
  ),
];
