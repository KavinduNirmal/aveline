import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import '../../core/providers/boutique_provider.dart';
import '../../features/search/data/api_search_repository.dart';
import '../../features/search/presentation/search_controller.dart' as app_search;

/// Full-screen search overlay providing global search UI across catalog,
/// customers, and conversations.
///
/// Can be embedded directly or shown modally using [SearchOverlay.show].
class SearchOverlay extends StatefulWidget {
  const SearchOverlay({
    super.key,
    this.onClose,
    this.onQueryChanged,
    this.repository,
    this.controller,
  });

  /// Called when the search overlay close button is tapped.
  final VoidCallback? onClose;

  /// Called whenever the search query text changes.
  final ValueChanged<String>? onQueryChanged;

  /// Explicit search repository, for testing or specialized scopes.
  final SearchRepository? repository;

  /// Explicit search controller, for testing.
  final app_search.SearchController? controller;

  /// Helper to push the [SearchOverlay] as a modal full-screen route.
  static Future<void> show(BuildContext context) {
    return Navigator.of(context).push(
      PageRouteBuilder<void>(
        pageBuilder: (context, animation, secondaryAnimation) =>
            SearchOverlay(onClose: () => Navigator.of(context).pop()),
        transitionsBuilder: (context, animation, secondaryAnimation, child) {
          final curved = CurvedAnimation(
            parent: animation,
            curve: Curves.easeOutCubic,
          );
          return FadeTransition(
            opacity: curved,
            child: SlideTransition(
              position: Tween<Offset>(
                begin: const Offset(0, -0.05),
                end: Offset.zero,
              ).animate(curved),
              child: child,
            ),
          );
        },
        opaque: true,
        barrierDismissible: false,
      ),
    );
  }

  @override
  State<SearchOverlay> createState() => _SearchOverlayState();
}

class _SearchOverlayState extends State<SearchOverlay> {
  final TextEditingController _textController = TextEditingController();
  final FocusNode _focusNode = FocusNode();
  late app_search.SearchController _searchController;
  bool _ownsController = false;

  @override
  void initState() {
    super.initState();
    _textController.addListener(_onTextChanged);
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    _resolveSearchController();
  }

  void _resolveSearchController() {
    if (widget.controller != null) {
      _searchController = widget.controller!;
      _ownsController = false;
      return;
    }

    if (widget.repository != null) {
      _searchController = app_search.SearchController(widget.repository!);
      _ownsController = true;
      return;
    }

    try {
      final dio = context.read<Dio>();
      final boutique = context.read<BoutiqueProvider>();
      final repo = ApiSearchRepository(
        dio,
        organizationId: () => boutique.organizationId,
      );
      _searchController = app_search.SearchController(repo);
      _ownsController = true;
    } catch (_) {
      _searchController = app_search.SearchController(const _EmptySearchRepository());
      _ownsController = true;
    }
  }

  @override
  void dispose() {
    _textController.removeListener(_onTextChanged);
    _textController.dispose();
    _focusNode.dispose();
    if (_ownsController) {
      _searchController.dispose();
    }
    super.dispose();
  }

  void _onTextChanged() {
    final text = _textController.text;
    widget.onQueryChanged?.call(text);
    _searchController.onQueryChanged(text);
    setState(() {});
  }

  void _handleClose() {
    if (widget.onClose != null) {
      widget.onClose!();
    } else {
      Navigator.of(context).maybePop();
    }
  }

  void _onItemTapped(SearchResultItem item) {
    _handleClose();
    try {
      context.go(item.href);
    } catch (_) {
      // Swallowed in test environments without a router in tree
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Scaffold(
      backgroundColor: scheme.surface,
      body: SafeArea(
        child: Column(
          children: [
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
              child: Row(
                children: [
                  Expanded(
                    child: Container(
                      height: 48,
                      decoration: BoxDecoration(
                        color: scheme.surfaceContainerHighest.withValues(alpha: 0.5),
                        borderRadius: BorderRadius.circular(24),
                        border: Border.all(
                          color: scheme.outlineVariant.withValues(alpha: 0.4),
                        ),
                      ),
                      padding: const EdgeInsets.symmetric(horizontal: 14),
                      child: Row(
                        children: [
                          Icon(
                            Icons.search_rounded,
                            color: scheme.onSurfaceVariant,
                            size: 20,
                          ),
                          const SizedBox(width: 10),
                          Expanded(
                            child: TextField(
                              controller: _textController,
                              focusNode: _focusNode,
                              autofocus: true,
                              style: theme.textTheme.bodyMedium?.copyWith(
                                color: scheme.onSurface,
                              ),
                              decoration: InputDecoration(
                                hintText: 'Search Aveline...',
                                hintStyle: theme.textTheme.bodyMedium?.copyWith(
                                  color: scheme.onSurfaceVariant.withValues(alpha: 0.7),
                                ),
                                border: InputBorder.none,
                                isDense: true,
                                contentPadding: EdgeInsets.zero,
                              ),
                            ),
                          ),
                          if (_textController.text.isNotEmpty)
                            GestureDetector(
                              onTap: () {
                                _textController.clear();
                                _searchController.clear();
                              },
                              child: Icon(
                                Icons.clear_rounded,
                                size: 18,
                                color: scheme.onSurfaceVariant,
                              ),
                            ),
                        ],
                      ),
                    ),
                  ),
                  const SizedBox(width: 10),
                  IconButton(
                    key: const Key('search_overlay_close_button'),
                    icon: const Icon(Icons.close_rounded),
                    color: scheme.onSurfaceVariant,
                    onPressed: _handleClose,
                    tooltip: 'Close search',
                  ),
                ],
              ),
            ),
            if (_searchController.isLoading)
              const LinearProgressIndicator(minHeight: 2),
            const Divider(height: 1, thickness: 0.5),
            Expanded(
              child: ListenableBuilder(
                listenable: _searchController,
                builder: (context, _) => _buildBody(theme, scheme),
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildBody(ThemeData theme, ColorScheme scheme) {
    if (_searchController.status == app_search.SearchStatus.idle ||
        _textController.text.trim().length < 2) {
      return Center(
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 32),
          child: Column(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              Icon(
                Icons.manage_search_rounded,
                size: 48,
                color: scheme.onSurfaceVariant.withValues(alpha: 0.35),
              ),
              const SizedBox(height: 16),
              Text(
                'Search across catalog, customers, and conversations',
                textAlign: TextAlign.center,
                style: theme.textTheme.bodyMedium?.copyWith(
                  color: scheme.onSurfaceVariant.withValues(alpha: 0.7),
                ),
              ),
            ],
          ),
        ),
      );
    }

    if (_searchController.status == app_search.SearchStatus.empty) {
      return Center(
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 32),
          child: Column(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              Icon(
                Icons.search_off_rounded,
                size: 48,
                color: scheme.onSurfaceVariant.withValues(alpha: 0.35),
              ),
              const SizedBox(height: 16),
              Text(
                'No results found for "${_textController.text.trim()}"',
                textAlign: TextAlign.center,
                style: theme.textTheme.bodyMedium?.copyWith(
                  color: scheme.onSurfaceVariant.withValues(alpha: 0.7),
                ),
              ),
            ],
          ),
        ),
      );
    }

    if (_searchController.status == app_search.SearchStatus.error) {
      return Center(
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 32),
          child: Text(
            'Could not complete search. Please try again.',
            textAlign: TextAlign.center,
            style: theme.textTheme.bodyMedium?.copyWith(
              color: scheme.error,
            ),
          ),
        ),
      );
    }

    final items = _searchController.results;
    return ListView.separated(
      itemCount: items.length,
      padding: const EdgeInsets.symmetric(vertical: 8),
      separatorBuilder: (context, index) => const Divider(height: 1, indent: 64),
      itemBuilder: (context, index) {
        final item = items[index];
        return ListTile(
          leading: CircleAvatar(
            backgroundColor: scheme.surfaceContainerHighest,
            foregroundColor: scheme.onSurfaceVariant,
            child: Icon(_iconForType(item.type), size: 20),
          ),
          title: Text(
            item.title,
            style: theme.textTheme.bodyLarge?.copyWith(
              fontWeight: FontWeight.w600,
            ),
          ),
          subtitle: item.subtitle != null && item.subtitle!.isNotEmpty
              ? Text(
                  item.subtitle!,
                  style: theme.textTheme.bodySmall?.copyWith(
                    color: scheme.onSurfaceVariant,
                  ),
                )
              : null,
          trailing: const Icon(Icons.arrow_forward_ios_rounded, size: 14),
          onTap: () => _onItemTapped(item),
        );
      },
    );
  }

  IconData _iconForType(String type) {
    switch (type.toLowerCase()) {
      case 'customer':
        return Icons.person_rounded;
      case 'catalogitem':
        return Icons.checkroom_rounded;
      case 'conversation':
        return Icons.chat_bubble_outline_rounded;
      default:
        return Icons.search_rounded;
    }
  }
}

class _EmptySearchRepository implements SearchRepository {
  const _EmptySearchRepository();

  @override
  Future<List<SearchResultItem>> search(String query, {String scope = 'all'}) async =>
      const [];
}
