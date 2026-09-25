import 'package:flutter/foundation.dart';

/// Domain entity representing a VIP client taste-profile affinity match for a catalog piece.
@immutable
class CustomerMatch {
  const CustomerMatch({
    required this.id,
    required this.customerId,
    required this.customerName,
    required this.customerEmail,
    this.customerAvatar,
    required this.matchConfidence,
    required this.matchReason,
    this.preferredColor,
    this.preferredFabric,
    this.preferredSize,
    this.employeeActed = false,
  });

  /// Factory constructor to parse backend DTO payload or demo dictionary.
  factory CustomerMatch.fromJson(Map<String, dynamic> json) {
    final rawScore = json['matchConfidence'] ?? json['confidence'] ?? json['score'] ?? 0.0;
    final score = rawScore is num ? rawScore.toDouble() : 0.0;

    return CustomerMatch(
      id: (json['id'] ?? json['matchId'] ?? '').toString(),
      customerId: (json['customerId'] ?? json['userId'] ?? '').toString(),
      customerName: (json['customerName'] ?? json['name'] ?? 'VIP Client').toString(),
      customerEmail: (json['customerEmail'] ?? json['email'] ?? '').toString(),
      customerAvatar: json['customerAvatar'] as String? ?? json['avatarUrl'] as String?,
      matchConfidence: score > 1.0 ? score / 100.0 : score,
      matchReason: (json['matchReason'] ?? json['reason'] ?? 'Matches past purchase palette and sizing.').toString(),
      preferredColor: json['preferredColor'] as String? ?? json['color'] as String?,
      preferredFabric: json['preferredFabric'] as String? ?? json['fabric'] as String?,
      preferredSize: json['preferredSize'] as String? ?? json['size'] as String?,
      employeeActed: json['employeeActed'] == true || json['isActed'] == true,
    );
  }

  final String id;
  final String customerId;
  final String customerName;
  final String customerEmail;
  final String? customerAvatar;

  /// Affinity confidence normalized between 0.0 and 1.0 (e.g. 0.94 -> 94%).
  final double matchConfidence;

  /// AI explanation of why this piece matches the VIP client.
  final String matchReason;

  final String? preferredColor;
  final String? preferredFabric;
  final String? preferredSize;

  /// Whether a boutique associate has already initiated outreach.
  final bool employeeActed;

  /// Integer percentage (e.g., 94).
  int get matchPercentage => (matchConfidence * 100).round().clamp(0, 100);

  /// Label for UI badge (e.g. "94% Match").
  String get matchScoreLabel => '$matchPercentage% Match';

  /// Score normalized for progress indicator (0.0 to 1.0).
  double get scoreProgress => matchConfidence.clamp(0.0, 1.0);

  CustomerMatch copyWith({
    String? id,
    String? customerId,
    String? customerName,
    String? customerEmail,
    String? customerAvatar,
    double? matchConfidence,
    String? matchReason,
    String? preferredColor,
    String? preferredFabric,
    String? preferredSize,
    bool? employeeActed,
  }) {
    return CustomerMatch(
      id: id ?? this.id,
      customerId: customerId ?? this.customerId,
      customerName: customerName ?? this.customerName,
      customerEmail: customerEmail ?? this.customerEmail,
      customerAvatar: customerAvatar ?? this.customerAvatar,
      matchConfidence: matchConfidence ?? this.matchConfidence,
      matchReason: matchReason ?? this.matchReason,
      preferredColor: preferredColor ?? this.preferredColor,
      preferredFabric: preferredFabric ?? this.preferredFabric,
      preferredSize: preferredSize ?? this.preferredSize,
      employeeActed: employeeActed ?? this.employeeActed,
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'id': id,
      'customerId': customerId,
      'customerName': customerName,
      'customerEmail': customerEmail,
      'customerAvatar': customerAvatar,
      'matchConfidence': matchConfidence,
      'matchReason': matchReason,
      'preferredColor': preferredColor,
      'preferredFabric': preferredFabric,
      'preferredSize': preferredSize,
      'employeeActed': employeeActed,
    };
  }

  @override
  bool operator ==(Object other) {
    if (identical(this, other)) return true;
    return other is CustomerMatch &&
        other.id == id &&
        other.customerId == customerId &&
        other.matchConfidence == matchConfidence &&
        other.employeeActed == employeeActed;
  }

  @override
  int get hashCode => Object.hash(id, customerId, matchConfidence, employeeActed);
}
