import 'package:flutter/foundation.dart';

/// Request payload to compose an ensemble lookbook around a primary piece using Elle AI.
@immutable
class ComposeOutfitPayload {
  const ComposeOutfitPayload({
    required this.name,
    required this.primaryItemId,
    this.notes,
    this.occasion,
  });

  /// The editorial title for the composed lookbook.
  final String name;

  /// The primary / anchor inventory item ID.
  final String primaryItemId;

  /// Optional stylist guidelines or occasion context.
  final String? notes;

  /// Target ceremonial occasion.
  final String? occasion;

  Map<String, dynamic> toJson() {
    return {
      'name': name,
      'primaryItemId': primaryItemId,
      if (notes != null && notes!.trim().isNotEmpty) 'notes': notes!.trim(),
      if (occasion != null && occasion!.trim().isNotEmpty) 'occasion': occasion!.trim(),
    };
  }

  @override
  bool operator ==(Object other) =>
      identical(this, other) ||
      other is ComposeOutfitPayload &&
          runtimeType == other.runtimeType &&
          name == other.name &&
          primaryItemId == other.primaryItemId &&
          notes == other.notes &&
          occasion == other.occasion;

  @override
  int get hashCode => Object.hash(name, primaryItemId, notes, occasion);
}

/// Request payload to update metadata on an existing composed lookbook.
@immutable
class UpdateLookbookPayload {
  const UpdateLookbookPayload({
    this.name,
    this.occasion,
    this.styleNotes,
  });

  /// Updated title of the lookbook.
  final String? name;

  /// Updated ceremonial occasion.
  final String? occasion;

  /// Updated stylist narrative / recommendations.
  final String? styleNotes;

  Map<String, dynamic> toJson() {
    return {
      if (name != null && name!.trim().isNotEmpty) 'name': name!.trim(),
      if (occasion != null && occasion!.trim().isNotEmpty) 'occasion': occasion!.trim(),
      if (styleNotes != null && styleNotes!.trim().isNotEmpty) 'styleNotes': styleNotes!.trim(),
    };
  }

  @override
  bool operator ==(Object other) =>
      identical(this, other) ||
      other is UpdateLookbookPayload &&
          runtimeType == other.runtimeType &&
          name == other.name &&
          occasion == other.occasion &&
          styleNotes == other.styleNotes;

  @override
  int get hashCode => Object.hash(name, occasion, styleNotes);
}
