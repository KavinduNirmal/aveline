import 'package:flutter/widgets.dart';

/// Configuration definition for a screen registered in an app shell (staff or owner).
class ScreenConfig {
  const ScreenConfig({
    required this.id,
    required this.label,
    required this.icon,
    required this.activeIcon,
    required this.route,
    this.permission,
    required this.builder,
  });

  /// Unique identifier for the screen (e.g. 'home', 'catalog', 'conversations').
  final String id;

  /// User-visible label for drawers or navigation surfaces.
  final String label;

  /// Inactive icon.
  final IconData icon;

  /// Active/selected icon.
  final IconData activeIcon;

  /// Route path (e.g. '/', '/catalog').
  final String route;

  /// Permission required to access or see this screen in navigation.
  /// When `null`, the screen is accessible to all authenticated users of this shell.
  final String? permission;

  /// Widget builder for the screen body.
  final Widget Function(BuildContext) builder;
}
