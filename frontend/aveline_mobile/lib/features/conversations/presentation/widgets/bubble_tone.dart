/// Which bubble a content block sits in.
///
/// The surface has to flip with it: a card painted for the neutral agent bubble
/// reads as a white patch on the associate's primary-filled bubble, and a mention
/// pill tinted with the bubble's own ink vanishes into it.
///
/// The web names this twice (`MentionTone` in `Mentions.tsx`, `AttachmentTone` in
/// `blocks.tsx`) because the two files are separate; one vocabulary covers both
/// here, since it is the same question in both places.
enum BubbleTone {
  /// The associate's own message.
  own,

  /// An agent's, or a forwarded customer's, message.
  other,
}
