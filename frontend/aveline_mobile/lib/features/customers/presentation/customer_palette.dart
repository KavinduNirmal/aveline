import 'package:flutter/material.dart';

import '../domain/customer_detail.dart';

/// The muted accents the concierge module's own vocabularies need a colour for.
///
/// The theme carries one accent (wine) and one error. This module classifies
/// things — what a memory is, what an occasion is, which channel a message came
/// through, whether the boutique may make contact — and painting all of them in
/// the same wine would spend colour without saying anything. Each mark therefore
/// gets a low-chroma tone in the brand's register, dark enough to carry a label
/// on paper.
const Color customerEmerald = Color(0xFF2F6B52);
const Color customerOchre = Color(0xFF8A6A2F);
const Color customerLilac = Color(0xFF6E5A7A);
const Color customerSlate = Color(0xFF4A5A6A);

/// Whether the boutique may make contact, and how that stands.
///
/// Consent is not the floor's to change: the API records it from the client's
/// own channel, so this colour is for reading, never for a control.
Color consentColor(ConsentStatus status, ColorScheme scheme) => switch (status) {
  ConsentStatus.granted => customerEmerald,
  ConsentStatus.pending => customerOchre,
  ConsentStatus.revoked => scheme.error,
  ConsentStatus.unknown => scheme.onSurfaceVariant,
};

IconData consentIcon(ConsentStatus status) => switch (status) {
  ConsentStatus.granted => Icons.verified_user_outlined,
  ConsentStatus.pending => Icons.hourglass_empty_rounded,
  ConsentStatus.revoked => Icons.block_outlined,
  ConsentStatus.unknown => Icons.help_outline_rounded,
};

/// What kind of thing a memory is.
///
/// A complaint reads as a warning and a preference as the brand's own accent,
/// because those are the two an associate has to act differently on.
Color memoryCategoryColor(MemoryCategory category, ColorScheme scheme) =>
    switch (category) {
      MemoryCategory.preference => scheme.primary,
      MemoryCategory.event => customerOchre,
      MemoryCategory.complaint => scheme.error,
      MemoryCategory.sentiment => customerLilac,
      MemoryCategory.fact => customerSlate,
    };

IconData memoryCategoryIcon(MemoryCategory category) => switch (category) {
  MemoryCategory.preference => Icons.favorite_outline,
  MemoryCategory.event => Icons.event_outlined,
  MemoryCategory.complaint => Icons.report_problem_outlined,
  MemoryCategory.sentiment => Icons.mood_outlined,
  MemoryCategory.fact => Icons.info_outline,
};

/// What the occasion is.
Color occasionColor(CustomerEventType type, ColorScheme scheme) =>
    switch (type) {
      CustomerEventType.wedding => scheme.primary,
      CustomerEventType.birthday => customerOchre,
      CustomerEventType.anniversary => customerLilac,
      CustomerEventType.party => customerSlate,
      CustomerEventType.office => scheme.tertiary,
      CustomerEventType.function => scheme.secondary,
      CustomerEventType.other => scheme.onSurfaceVariant,
    };

IconData occasionIcon(CustomerEventType type) => switch (type) {
  CustomerEventType.wedding => Icons.favorite_outline,
  CustomerEventType.birthday => Icons.cake_outlined,
  CustomerEventType.anniversary => Icons.celebration_outlined,
  CustomerEventType.party => Icons.nightlife_outlined,
  CustomerEventType.office => Icons.work_outline,
  CustomerEventType.function => Icons.event_outlined,
  CustomerEventType.other => Icons.event_outlined,
};

/// Where an exchange happened.
Color channelColor(InteractionChannel channel, ColorScheme scheme) =>
    switch (channel) {
      InteractionChannel.whatsapp => customerEmerald,
      InteractionChannel.instagram => customerLilac,
      InteractionChannel.inPerson => scheme.primary,
      InteractionChannel.phone => customerSlate,
    };

IconData channelIcon(InteractionChannel channel) => switch (channel) {
  InteractionChannel.whatsapp => Icons.chat_bubble_outline,
  InteractionChannel.instagram => Icons.photo_camera_outlined,
  InteractionChannel.inPerson => Icons.storefront_outlined,
  InteractionChannel.phone => Icons.call_outlined,
};

/// How sure the boutique is of something it believes about a client.
///
/// Read as a rule of thumb: the brand accent for what the client stated or the
/// agent is confident of, and the quiet slate for a guess.
Color confidenceColor(double confidence, ColorScheme scheme) =>
    confidence >= 0.8 ? scheme.primary : customerSlate;
