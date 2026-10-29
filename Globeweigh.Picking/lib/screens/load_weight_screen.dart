import 'dart:async';

import 'package:flutter/material.dart';

import '../api/api_client.dart';
import '../services/session.dart';
import '../models/weight_reading.dart';
import '../theme/app_colors.dart';

/// Capture Weight — the final Complete Load step, after the photos. Shows ONE live
/// weighbridge reading at a time (defaulting to Bridge 2, switchable to Bridge 1),
/// polled every second like TauriAdmin's weigh screens. Capture Weight records the
/// current reading as the load's out-weight, which completes the load.
class LoadWeightScreen extends StatefulWidget {
  final int recordId;
  final String? haulierName;

  const LoadWeightScreen({super.key, required this.recordId, this.haulierName});

  @override
  State<LoadWeightScreen> createState() => _LoadWeightScreenState();
}

class _LoadWeightScreenState extends State<LoadWeightScreen> {
  final _api = ApiClient();
  int _bridge = 2; // default Bridge 2
  WeightReading? _reading;
  Timer? _poll;
  bool _saving = false;

  static const Color _green = Color(0xFF16A34A);

  bool get _live => _reading?.isLive == true;

  @override
  void initState() {
    super.initState();
    _tick();
    _poll = Timer.periodic(const Duration(seconds: 1), (_) => _tick());
  }

  @override
  void dispose() {
    _poll?.cancel();
    super.dispose();
  }

  Future<void> _tick() async {
    final bridge = _bridge;
    final r = await _api.getLiveWeight(bridge: bridge);
    // A slow response for the previously-selected bridge must not clobber the
    // current one's reading.
    if (!mounted || bridge != _bridge) return;
    setState(() => _reading = r);
  }

  void _setBridge(int bridge) {
    if (_bridge == bridge) return;
    setState(() {
      _bridge = bridge;
      _reading = null; // show "—" until the newly-selected bridge answers
    });
    _tick();
  }

  Future<void> _capture() async {
    final kg = _reading?.kg;
    if (!_live || kg == null || _saving) return;
    setState(() => _saving = true);
    // Grabbed before the await, and before the pops: this screen and the photos screen
    // behind it both come off the stack, so a messenger looked up afterwards would be
    // gone. The app-level messenger keeps the bar on screen across both pops, which is
    // the point — the picker reads it back on Pending Loads.
    final messenger = ScaffoldMessenger.of(context);
    try {
      final result =
          await _api.completeLoad(
              recordId: widget.recordId, weight: kg, userId: Session.userId);
      if (!mounted) return;
      messenger.showSnackBar(
        SnackBar(
          content: Text(result.message,
              style: const TextStyle(fontFamily: 'Poppins', fontSize: 14)),
          duration: const Duration(seconds: 5),
        ),
      );
      Navigator.of(context).pop(true);
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() => _saving = false);
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(e.message,
              style: const TextStyle(fontFamily: 'Poppins', fontSize: 14)),
        ),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    final kg = _reading?.kg;
    return Scaffold(
      backgroundColor: AppColors.background,
      appBar: AppBar(
        backgroundColor: AppColors.navy,
        foregroundColor: Colors.white,
        surfaceTintColor: Colors.transparent,
        title: Text(
          widget.haulierName == null
              ? 'Capture Weight'
              : 'Capture Weight — ${widget.haulierName}',
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          style: const TextStyle(
            fontFamily: 'Poppins',
            fontWeight: FontWeight.w700,
            color: Colors.white,
          ),
        ),
      ),
      body: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Expanded(
              child: Container(
                decoration: BoxDecoration(
                  color: AppColors.surface,
                  borderRadius: BorderRadius.circular(12),
                  border: Border.all(color: AppColors.borderLight),
                ),
                // Nudged 50px above true centre.
                child: Transform.translate(
                  offset: const Offset(0, -50),
                  child: Column(
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                      _bridgeSelector(),
                      const SizedBox(height: 28),
                      _gauge(kg),
                    ],
                  ),
                ),
              ),
            ),
            const SizedBox(height: 12),
            Align(
              alignment: Alignment.centerRight,
              child: ElevatedButton.icon(
                icon: const Icon(Icons.check_circle_outline, size: 22),
                label: Text(_saving ? 'Completing…' : 'Weigh / Complete'),
                style: ElevatedButton.styleFrom(
                  backgroundColor: _green,
                  foregroundColor: Colors.white,
                  disabledBackgroundColor: AppColors.borderLight,
                  disabledForegroundColor: AppColors.textMuted,
                  minimumSize: const Size(220, 52),
                  textStyle: const TextStyle(
                    fontFamily: 'Poppins',
                    fontSize: 15,
                    fontWeight: FontWeight.w700,
                  ),
                ),
                onPressed: _live && !_saving ? _capture : null,
              ),
            ),
          ],
        ),
      ),
    );
  }

  /// The live-weight readout — dark green "7-segment" gauge, same look as
  /// TauriTouch's Manual Stock Add Pallet popup (.tt-gauge in TouchScreen.css):
  /// near-black green-tinted box with glowing green monospace digits, greyed
  /// (no glow) with "----" while the reading is stale.
  Widget _gauge(int? kg) {
    const digitGreen = Color(0xFF2BFF8A);
    const staleGrey = Color(0xFF6B7280);
    final showValue = _live && kg != null;
    return Container(
      // Fixed width sized for a 5-digit reading (e.g. 50000) so the panel doesn't
      // resize as the live weight changes.
      width: 380,
      padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 22),
      decoration: BoxDecoration(
        color: const Color(0xFF03110A),
        border: Border.all(color: const Color(0xFF0C3B22)),
        borderRadius: BorderRadius.circular(14),
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Text(
            showValue ? '$kg' : '----',
            style: TextStyle(
              fontFamily: 'Consolas',
              fontFamilyFallback: const ['monospace'],
              fontSize: 80,
              fontWeight: FontWeight.w700,
              letterSpacing: 2,
              height: 1.1,
              color: showValue ? digitGreen : staleGrey,
              fontFeatures: const [FontFeature.tabularFigures()],
              shadows: showValue
                  ? const [
                      Shadow(
                        color: Color(0xB32BFF8A),
                        blurRadius: 14,
                      ),
                    ]
                  : null,
            ),
          ),
          const SizedBox(height: 2),
          Text(
            'kg',
            style: TextStyle(
              fontFamily: 'Poppins',
              fontSize: 16,
              color: digitGreen.withValues(alpha: 0.7),
            ),
          ),
          const SizedBox(height: 6),
          Text(
            _live
                ? 'Live weight  ·  Bridge $_bridge'
                : 'Waiting for Bridge $_bridge…',
            style: TextStyle(
              fontFamily: 'Poppins',
              fontSize: 12,
              fontWeight: FontWeight.w500,
              color: _live ? const Color(0xFF93A0B4) : const Color(0xFFD97706),
            ),
          ),
        ],
      ),
    );
  }

  /// The selected bridge as a label, with a deliberate two-step switch: a small
  /// "Change" button that asks for confirmation first, so the bridge can't be
  /// flipped with one accidental tap.
  Widget _bridgeSelector() {
    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        Container(
          padding: const EdgeInsets.symmetric(horizontal: 22, vertical: 10),
          decoration: BoxDecoration(
            color: AppColors.primary.withValues(alpha: 0.10),
            borderRadius: BorderRadius.circular(10),
            border: Border.all(color: AppColors.primary.withValues(alpha: 0.35)),
          ),
          child: Text(
            'Bridge $_bridge',
            style: const TextStyle(
              fontFamily: 'Poppins',
              fontSize: 16,
              fontWeight: FontWeight.w700,
              color: AppColors.primary,
            ),
          ),
        ),
        const SizedBox(width: 10),
        TextButton(
          onPressed: _saving ? null : _confirmSwitch,
          style: TextButton.styleFrom(
            foregroundColor: AppColors.textLight,
            padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
            textStyle: const TextStyle(
              fontFamily: 'Poppins',
              fontSize: 13.5,
              fontWeight: FontWeight.w600,
            ),
          ),
          child: const Text('Change'),
        ),
      ],
    );
  }

  Future<void> _confirmSwitch() async {
    final other = _bridge == 2 ? 1 : 2;
    final ok = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        icon: const Icon(Icons.swap_horiz, color: AppColors.primary, size: 40),
        title: Text('Switch to Bridge $other?',
            style: const TextStyle(
                fontFamily: 'Poppins', fontWeight: FontWeight.w700)),
        content: Text(
          'The live weight will be read from Bridge $other instead of Bridge $_bridge.',
          textAlign: TextAlign.center,
          style: const TextStyle(fontFamily: 'Poppins'),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(ctx).pop(false),
            child: const Text('Cancel'),
          ),
          TextButton(
            onPressed: () => Navigator.of(ctx).pop(true),
            child: Text('Switch to Bridge $other'),
          ),
        ],
      ),
    );
    if (ok == true && mounted) _setBridge(other);
  }
}
