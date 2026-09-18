import '../domain/conversation.dart';

/// The boutique's message inbox: the Salon, and every client thread.
///
/// Deliberately unordered. Which thread belongs at the top is a decision the
/// inbox makes once, in its controller, rather than a rule every source has to
/// remember to apply.
abstract interface class ConversationRepository {
  /// Every conversation the caller can see.
  Future<List<Conversation>> fetchConversations();
}
