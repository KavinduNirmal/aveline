import '../domain/conversation.dart';
import 'conversation_repository.dart';

/// An inbox with nothing in it, used wherever no real source has been injected.
///
/// The screen used to fall back to the demo repository, which put eight invented
/// threads on a production path (the drawer builds `const ConversationsScreen()`
/// with no way to inject anything). This repository is the honest stand-in: no
/// data, so the screen renders its real empty state rather than fiction.
class EmptyConversationRepository implements ConversationRepository {
  const EmptyConversationRepository();

  @override
  Future<ConversationPage> fetchConversations({int page = 1}) async =>
      ConversationPage.empty;

  @override
  Future<Conversation?> fetchConversation(String id) async => null;
}
