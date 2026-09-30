import 'package:flutter/material.dart';

/// Pipeline stages for bespoke client sourcing requests with partner ateliers.
enum SourcingStatus {
  pending,
  quoted,
  approved,
  ordered,
  fulfilled,
  archived;

  /// Parses wire string or default to [pending].
  static SourcingStatus fromWire(String? raw) {
    if (raw == null) return SourcingStatus.pending;
    final normalized = raw.trim().toLowerCase();
    switch (normalized) {
      case 'quoted':
        return SourcingStatus.quoted;
      case 'approved':
        return SourcingStatus.approved;
      case 'ordered':
        return SourcingStatus.ordered;
      case 'fulfilled':
        return SourcingStatus.fulfilled;
      case 'archived':
        return SourcingStatus.archived;
      case 'pending':
      default:
        return SourcingStatus.pending;
    }
  }

  /// Converts enum to backend wire string representation.
  String toWire() => name;

  /// Human-readable luxury pipeline stage label matching web nomenclature.
  String get label {
    switch (this) {
      case SourcingStatus.pending:
        return 'Pending Quote';
      case SourcingStatus.quoted:
        return 'Quoted by Atelier';
      case SourcingStatus.approved:
        return 'Approved';
      case SourcingStatus.ordered:
        return 'Ordered from Atelier';
      case SourcingStatus.fulfilled:
        return 'Fulfilled';
      case SourcingStatus.archived:
        return 'Archived';
    }
  }

  /// Short stage badge label.
  String get shortLabel {
    switch (this) {
      case SourcingStatus.pending:
        return 'Pending';
      case SourcingStatus.quoted:
        return 'Quoted';
      case SourcingStatus.approved:
        return 'Approved';
      case SourcingStatus.ordered:
        return 'Ordered';
      case SourcingStatus.fulfilled:
        return 'Fulfilled';
      case SourcingStatus.archived:
        return 'Archived';
    }
  }

  /// Color for badge background and foreground.
  Color get badgeColor {
    switch (this) {
      case SourcingStatus.pending:
        return const Color(0xFFD97706); // Amber / Warning
      case SourcingStatus.quoted:
        return const Color(0xFF2563EB); // Blue
      case SourcingStatus.approved:
        return const Color(0xFF7C3AED); // Purple / Violet
      case SourcingStatus.ordered:
        return const Color(0xFF0D9488); // Teal
      case SourcingStatus.fulfilled:
        return const Color(0xFF16A34A); // Emerald / Success
      case SourcingStatus.archived:
        return const Color(0xFF6B7280); // Gray / Muted
    }
  }

  /// Background tint color for capsule pills and stage headers.
  Color badgeBackgroundColor(bool isDark) {
    switch (this) {
      case SourcingStatus.pending:
        return const Color(0xFFFEF3C7);
      case SourcingStatus.quoted:
        return const Color(0xFFDBEAFE);
      case SourcingStatus.approved:
        return const Color(0xFFEDE9FE);
      case SourcingStatus.ordered:
        return const Color(0xFFCCFBF1);
      case SourcingStatus.fulfilled:
        return const Color(0xFFDCFCE7);
      case SourcingStatus.archived:
        return const Color(0xFFF3F4F6);
    }
  }

  /// Whether this ticket is active in the work-in-progress pipeline.
  bool get isActive => this != SourcingStatus.archived;
}
