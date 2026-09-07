import 'package:aveline_mobile/core/providers/owner_onboarding_provider.dart';
import 'package:aveline_mobile/core/providers/user_provider.dart';
import 'package:aveline_mobile/core/router/route_guards.dart';
import 'package:aveline_mobile/features/auth/domain/aveline_user.dart';
import 'package:aveline_mobile/features/onboarding/data/owner_onboarding_api.dart';
import 'package:aveline_mobile/features/onboarding/domain/owner_onboarding.dart';
import 'package:aveline_mobile/shared/widgets/aurora_field.dart';
import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

/// Owner onboarding wizard: boutique details → plan → AI context → complete.
///
/// Resumes from `GET /status` when a draft organization already exists.
class OwnerOnboardingScreen extends StatefulWidget {
  const OwnerOnboardingScreen({super.key});

  @override
  State<OwnerOnboardingScreen> createState() => _OwnerOnboardingScreenState();
}

class _OwnerOnboardingScreenState extends State<OwnerOnboardingScreen> {
  int _step = 0;
  bool _hydrated = false;

  static const _stepTitles = [
    'Boutique details',
    'Choose your plan',
    'Customize your AI',
    'Review & launch',
  ];

  @override
  void initState() {
    super.initState();
    _hydrate();
  }

  Future<void> _hydrate() async {
    final provider = context.read<OwnerOnboardingProvider>();
    await provider.loadStatus();
    if (!mounted) return;
    setState(() {
      _hydrated = true;
      // Backend steps: 3=boutique, 4=plan, 5=AI, 6=complete.
      final backendStep = provider.currentStep;
      if (backendStep >= 6) {
        _step = 3;
      } else if (backendStep >= 5) {
        _step = 2;
      } else if (backendStep >= 4) {
        _step = 1;
      } else {
        _step = 0;
      }
    });
  }

  void _goTo(int step) {
    setState(() => _step = step);
  }

  Future<void> _finish() async {
    final provider = context.read<OwnerOnboardingProvider>();
    final userProvider = context.read<UserProvider>();
    final dio = context.read<Dio>();
    await provider.complete();
    // Refresh the user profile so the account state reflects the new active org.
    await userProvider.fetchUser(dio);
    if (!mounted) return;
    final active =
        userProvider.user?.accountState == AvelineAccountState.active;
    context.go(active ? AppRoutes.home : AppRoutes.orgSetup);
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Scaffold(
      appBar: AppBar(
        title: Text(
          'AVELINE',
          style: theme.textTheme.labelMedium?.copyWith(
            letterSpacing: 2.0,
            color: scheme.primary,
          ),
        ),
        centerTitle: true,
      ),
      body: Stack(
        children: [
          const Positioned.fill(child: AuroraField()),
          SafeArea(
            child: Center(
              child: SingleChildScrollView(
                padding: const EdgeInsets.symmetric(
                  horizontal: 24.0,
                  vertical: 16.0,
                ),
                child: ConstrainedBox(
                  constraints: const BoxConstraints(maxWidth: 480),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      Text(
                        'Set up your boutique',
                        style: theme.textTheme.headlineMedium?.copyWith(
                          color: scheme.onSurface,
                        ),
                        textAlign: TextAlign.center,
                      ),
                      const SizedBox(height: 8),
                      Text(
                        'Step ${_step + 1} of 4 — ${_stepTitles[_step]}',
                        style: theme.textTheme.bodyMedium?.copyWith(
                          color: scheme.onSurfaceVariant,
                        ),
                        textAlign: TextAlign.center,
                      ),
                      const SizedBox(height: 16),
                      _StepIndicator(current: _step, total: 4),
                      const SizedBox(height: 24),
                      if (!_hydrated)
                        const Padding(
                          padding: EdgeInsets.symmetric(vertical: 48),
                          child: Center(child: CircularProgressIndicator()),
                        )
                      else
                        switch (_step) {
                          0 => _BoutiqueDetailsStep(onNext: () => _goTo(1)),
                          1 => _PlanSelectionStep(onNext: () => _goTo(2)),
                          2 => _AiCustomizationStep(onNext: () => _goTo(3)),
                          _ => _ReviewStep(onFinish: _finish),
                        },
                    ],
                  ),
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _StepIndicator extends StatelessWidget {
  const _StepIndicator({required this.current, required this.total});

  final int current;
  final int total;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Row(
      mainAxisAlignment: MainAxisAlignment.center,
      children: List.generate(total, (i) {
        final active = i <= current;
        return Container(
          width: 28,
          height: 4,
          margin: const EdgeInsets.symmetric(horizontal: 3),
          decoration: BoxDecoration(
            color: active ? scheme.primary : scheme.surfaceContainerHighest,
            borderRadius: BorderRadius.circular(2),
          ),
        );
      }),
    );
  }
}

class _BoutiqueDetailsStep extends StatefulWidget {
  const _BoutiqueDetailsStep({required this.onNext});

  final VoidCallback onNext;

  @override
  State<_BoutiqueDetailsStep> createState() => _BoutiqueDetailsStepState();
}

class _BoutiqueDetailsStepState extends State<_BoutiqueDetailsStep> {
  final _formKey = GlobalKey<FormState>();
  late final TextEditingController _nameController;
  late final TextEditingController _addressController;
  late final TextEditingController _phoneController;
  late final TextEditingController _descriptionController;
  bool _submitting = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    final org = context.read<OwnerOnboardingProvider>().organization;
    _nameController = TextEditingController(text: org?.name ?? '');
    _addressController = TextEditingController(text: org?.address ?? '');
    _phoneController = TextEditingController(text: org?.phoneNumber ?? '');
    _descriptionController = TextEditingController(
      text: org?.description ?? '',
    );
  }

  @override
  void dispose() {
    _nameController.dispose();
    _addressController.dispose();
    _phoneController.dispose();
    _descriptionController.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (!(_formKey.currentState?.validate() ?? false)) return;
    setState(() {
      _submitting = true;
      _error = null;
    });
    try {
      final provider = context.read<OwnerOnboardingProvider>();
      await provider.saveBoutiqueDetails(
        SaveBoutiqueDetailsRequest(
          name: _nameController.text.trim(),
          address: _addressController.text.trim(),
          phoneNumber: _phoneController.text.trim(),
          description: _descriptionController.text.trim().isEmpty
              ? null
              : _descriptionController.text.trim(),
        ),
      );
      if (mounted) widget.onNext();
    } catch (e) {
      if (mounted) {
        setState(() => _error = e.toString().replaceFirst('Exception: ', ''));
      }
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Form(
      key: _formKey,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          if (_error != null) ...[
            _ErrorBanner(message: _error!),
            const SizedBox(height: 16),
          ],
          TextFormField(
            controller: _nameController,
            textCapitalization: TextCapitalization.words,
            decoration: const InputDecoration(
              labelText: 'Boutique name *',
              hintText: 'e.g. Aveline Boutique Colombo',
              prefixIcon: Icon(Icons.storefront_outlined),
            ),
            validator: (v) => (v == null || v.trim().isEmpty)
                ? 'Enter your boutique name'
                : null,
          ),
          const SizedBox(height: 16),
          TextFormField(
            controller: _addressController,
            maxLines: 2,
            decoration: const InputDecoration(
              labelText: 'Boutique address *',
              hintText: 'e.g. 15 Alfred House Gardens, Colombo 03',
              prefixIcon: Icon(Icons.location_on_outlined),
            ),
            validator: (v) => (v == null || v.trim().isEmpty)
                ? 'Enter your boutique address'
                : null,
          ),
          const SizedBox(height: 16),
          TextFormField(
            controller: _phoneController,
            keyboardType: TextInputType.phone,
            decoration: const InputDecoration(
              labelText: 'Contact phone / WhatsApp *',
              hintText: '+94 77 123 4567',
              prefixIcon: Icon(Icons.phone_outlined),
            ),
            validator: (v) => (v == null || v.trim().isEmpty)
                ? 'Enter a contact phone number'
                : null,
          ),
          const SizedBox(height: 16),
          TextFormField(
            controller: _descriptionController,
            maxLines: 3,
            decoration: const InputDecoration(
              labelText: 'Description (optional)',
              hintText: 'A short note about your boutique',
              prefixIcon: Icon(Icons.notes_outlined),
            ),
          ),
          const SizedBox(height: 28),
          FilledButton(
            onPressed: _submitting ? null : _submit,
            child: Padding(
              padding: const EdgeInsets.symmetric(vertical: 14),
              child: _submitting
                  ? const SizedBox(
                      height: 20,
                      width: 20,
                      child: CircularProgressIndicator(
                        strokeWidth: 2,
                        color: Colors.white,
                      ),
                    )
                  : const Text('Continue'),
            ),
          ),
        ],
      ),
    );
  }
}

class _PlanSelectionStep extends StatelessWidget {
  const _PlanSelectionStep({required this.onNext});

  final VoidCallback onNext;

  static const _plans = [
    (PlanTier.seed, 'Seed', 'Free', '150 blossoms / mo'),
    (PlanTier.bloom, 'Bloom', 'LKR 3,500 / mo', '750 blossoms / mo'),
    (PlanTier.orchid, 'Orchid', 'LKR 9,000 / mo', '2,000 blossoms / mo'),
    (PlanTier.rose, 'Rose', 'LKR 20,000 / mo', '5,000 blossoms / mo'),
  ];

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final provider = context.watch<OwnerOnboardingProvider>();
    final selected = provider.planTier;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Container(
          padding: const EdgeInsets.all(12),
          decoration: BoxDecoration(
            color: scheme.secondaryContainer.withValues(alpha: 0.5),
            borderRadius: BorderRadius.circular(12),
          ),
          child: Text(
            'Demo mode — no payment required. You\'ll be contacted for payment.',
            style: theme.textTheme.bodySmall?.copyWith(
              color: scheme.onSecondaryContainer,
            ),
            textAlign: TextAlign.center,
          ),
        ),
        const SizedBox(height: 16),
        ..._plans.map((plan) {
          final isSelected = selected == plan.$1;
          return Padding(
            padding: const EdgeInsets.only(bottom: 12),
            child: Card(
              elevation: 0,
              shape: RoundedRectangleBorder(
                borderRadius: BorderRadius.circular(14),
                side: BorderSide(
                  color: isSelected ? scheme.primary : scheme.outlineVariant,
                  width: isSelected ? 2 : 1,
                ),
              ),
              child: InkWell(
                borderRadius: BorderRadius.circular(14),
                onTap: () => provider.selectPlan(plan.$1),
                child: Padding(
                  padding: const EdgeInsets.symmetric(
                    horizontal: 16,
                    vertical: 14,
                  ),
                  child: Row(
                    children: [
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(
                              plan.$2,
                              style: theme.textTheme.titleMedium?.copyWith(
                                fontWeight: FontWeight.w600,
                              ),
                            ),
                            const SizedBox(height: 2),
                            Text(
                              '${plan.$3} · ${plan.$4}',
                              style: theme.textTheme.bodySmall?.copyWith(
                                color: scheme.onSurfaceVariant,
                              ),
                            ),
                          ],
                        ),
                      ),
                      Icon(
                        isSelected
                            ? Icons.check_circle
                            : Icons.radio_button_unchecked,
                        color: isSelected ? scheme.primary : scheme.outline,
                      ),
                    ],
                  ),
                ),
              ),
            ),
          );
        }),
        const SizedBox(height: 12),
        FilledButton(
          onPressed: provider.isLoading ? null : onNext,
          child: const Padding(
            padding: EdgeInsets.symmetric(vertical: 14),
            child: Text('Continue'),
          ),
        ),
      ],
    );
  }
}

class _AiCustomizationStep extends StatefulWidget {
  const _AiCustomizationStep({required this.onNext});

  final VoidCallback onNext;

  @override
  State<_AiCustomizationStep> createState() => _AiCustomizationStepState();
}

class _AiCustomizationStepState extends State<_AiCustomizationStep> {
  late final TextEditingController _brandVoiceController;
  late final TextEditingController _businessRulesController;
  late final TextEditingController _fabricsController;
  late final TextEditingController _preferencesController;
  bool _submitting = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    final org = context.read<OwnerOnboardingProvider>().organization;
    _brandVoiceController = TextEditingController(text: org?.brandVoice ?? '');
    _businessRulesController = TextEditingController(
      text: org?.businessRules ?? '',
    );
    _fabricsController = TextEditingController(
      text: org?.preferredColorsFabrics ?? '',
    );
    _preferencesController = TextEditingController(
      text: org?.customerPreferences ?? '',
    );
  }

  @override
  void dispose() {
    _brandVoiceController.dispose();
    _businessRulesController.dispose();
    _fabricsController.dispose();
    _preferencesController.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    setState(() {
      _submitting = true;
      _error = null;
    });
    try {
      final provider = context.read<OwnerOnboardingProvider>();
      await provider.saveAiCustomization(
        SaveAiCustomizationRequest(
          brandVoice: _brandVoiceController.text.trim().isEmpty
              ? null
              : _brandVoiceController.text.trim(),
          businessRules: _businessRulesController.text.trim().isEmpty
              ? null
              : _businessRulesController.text.trim(),
          preferredColorsFabrics: _fabricsController.text.trim().isEmpty
              ? null
              : _fabricsController.text.trim(),
          customerPreferences: _preferencesController.text.trim().isEmpty
              ? null
              : _preferencesController.text.trim(),
        ),
      );
      if (mounted) widget.onNext();
    } catch (e) {
      if (mounted) {
        setState(() => _error = e.toString().replaceFirst('Exception: ', ''));
      }
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final provider = context.watch<OwnerOnboardingProvider>();
    final tier = provider.planTier;
    final seedLocked = tier == PlanTier.seed;
    final bloomBasic = tier == PlanTier.bloom;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        if (_error != null) ...[
          _ErrorBanner(message: _error!),
          const SizedBox(height: 16),
        ],
        Text(
          'Customize how your AI concierge speaks and works for your boutique.',
          style: theme.textTheme.bodyMedium?.copyWith(
            color: scheme.onSurfaceVariant,
          ),
        ),
        const SizedBox(height: 16),
        _AiField(
          controller: _brandVoiceController,
          label: 'Brand voice',
          hint: 'Tone, personality, and how you address customers',
          locked: seedLocked,
        ),
        const SizedBox(height: 16),
        _AiField(
          controller: _businessRulesController,
          label: 'Business rules',
          hint: 'Discount limits, delivery rules, return policy',
          locked: seedLocked,
        ),
        const SizedBox(height: 16),
        _AiField(
          controller: _fabricsController,
          label: 'Preferred colors & fabrics',
          hint: 'Specialties your boutique is known for',
          locked: seedLocked || bloomBasic,
        ),
        const SizedBox(height: 16),
        _AiField(
          controller: _preferencesController,
          label: 'Customer memory preferences',
          hint: 'How the concierge remembers and serves customers',
          locked: seedLocked || bloomBasic,
        ),
        const SizedBox(height: 28),
        FilledButton(
          onPressed: _submitting ? null : _submit,
          child: Padding(
            padding: const EdgeInsets.symmetric(vertical: 14),
            child: _submitting
                ? const SizedBox(
                    height: 20,
                    width: 20,
                    child: CircularProgressIndicator(
                      strokeWidth: 2,
                      color: Colors.white,
                    ),
                  )
                : const Text('Continue'),
          ),
        ),
      ],
    );
  }
}

class _AiField extends StatelessWidget {
  const _AiField({
    required this.controller,
    required this.label,
    required this.hint,
    required this.locked,
  });

  final TextEditingController controller;
  final String label;
  final String hint;
  final bool locked;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return TextFormField(
      controller: controller,
      maxLines: 2,
      enabled: !locked,
      decoration: InputDecoration(
        labelText: locked ? '$label (locked on this plan)' : label,
        hintText: hint,
        prefixIcon: Icon(
          locked ? Icons.lock_outline : Icons.auto_awesome_outlined,
          color: locked ? scheme.outline : null,
        ),
      ),
    );
  }
}

class _ReviewStep extends StatelessWidget {
  const _ReviewStep({required this.onFinish});

  final VoidCallback onFinish;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final provider = context.watch<OwnerOnboardingProvider>();
    final org = provider.organization;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Card(
          elevation: 0,
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(14),
            side: BorderSide(color: scheme.outlineVariant),
          ),
          child: Padding(
            padding: const EdgeInsets.all(20),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                _ReviewRow(label: 'Boutique', value: org?.name ?? '—'),
                _ReviewRow(label: 'Address', value: org?.address ?? '—'),
                _ReviewRow(
                  label: 'Plan',
                  value: org?.planTier.wireValue ?? '—',
                ),
                _ReviewRow(
                  label: 'Blossoms',
                  value: _blossomLabel(org?.planTier),
                ),
              ],
            ),
          ),
        ),
        const SizedBox(height: 24),
        FilledButton(
          onPressed: provider.isLoading ? null : onFinish,
          child: Padding(
            padding: const EdgeInsets.symmetric(vertical: 14),
            child: provider.isLoading
                ? const SizedBox(
                    height: 20,
                    width: 20,
                    child: CircularProgressIndicator(
                      strokeWidth: 2,
                      color: Colors.white,
                    ),
                  )
                : const Text('Launch my boutique'),
          ),
        ),
        const SizedBox(height: 8),
        Text(
          'This activates your boutique, provisions your Blossom allowance, and warms up your AI concierge.',
          style: theme.textTheme.bodySmall?.copyWith(
            color: scheme.onSurfaceVariant,
          ),
          textAlign: TextAlign.center,
        ),
      ],
    );
  }

  String _blossomLabel(PlanTier? tier) {
    switch (tier) {
      case PlanTier.seed:
        return '150 / mo';
      case PlanTier.bloom:
        return '750 / mo';
      case PlanTier.orchid:
        return '2,000 / mo';
      case PlanTier.rose:
        return '5,000 / mo';
      case PlanTier.enterprise:
        return '9,999 / mo';
      case null:
        return '—';
    }
  }
}

class _ReviewRow extends StatelessWidget {
  const _ReviewRow({required this.label, required this.value});

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 6),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SizedBox(
            width: 90,
            child: Text(
              label,
              style: theme.textTheme.bodySmall?.copyWith(
                color: scheme.onSurfaceVariant,
              ),
            ),
          ),
          Expanded(
            child: Text(
              value,
              style: theme.textTheme.bodyMedium?.copyWith(
                color: scheme.onSurface,
                fontWeight: FontWeight.w600,
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _ErrorBanner extends StatelessWidget {
  const _ErrorBanner({required this.message});

  final String message;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    return Container(
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: scheme.errorContainer,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: scheme.error),
      ),
      child: Text(
        message,
        style: theme.textTheme.bodyMedium?.copyWith(
          color: scheme.onErrorContainer,
        ),
      ),
    );
  }
}
