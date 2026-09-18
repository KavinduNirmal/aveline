import 'package:flutter/material.dart';

/// Muted paper tints for the avatar circles, picked per person so a column of
/// them reads as a set of distinct people rather than as identical dots.
///
/// The API carries no photograph for a client, so a circle falls back to initials
/// the way a phone's contact list does. The tints are the brand's own quiet paper
/// tones rather than a colour per letter: two people who share an initial are
/// still two different people.
const List<Color> avatarTints = [
  Color(0xFFE6E1D8),
  Color(0xFFDCE6E1),
  Color(0xFFEDE3D6),
  Color(0xFFE2E2DE),
  Color(0xFFE9DEE2),
  Color(0xFFDEE3E8),
];

/// The tint for [name], picked from the name so it never moves.
///
/// Shared by the client book and the message inbox, so the same client is the
/// same colour in both: a face that changed shade between two screens would read
/// as a different person.
Color avatarTintFor(String name) {
  if (name.isEmpty) {
    return avatarTints.first;
  }
  final seed = name.codeUnits.fold<int>(0, (sum, unit) => sum + unit);
  return avatarTints[seed % avatarTints.length];
}
