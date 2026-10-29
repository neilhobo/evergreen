import 'dart:async';

import 'package:flutter/foundation.dart' show kDebugMode;
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../format.dart';
import '../models/pick_item.dart';
import '../models/scanned_pallet.dart';
import '../services/scanner.dart';
import '../theme/app_colors.dart';

/// Detail for one product line on a load — a header with the item + picking progress,
/// a live list of the pallets scanned for it (newest first), and a scan panel for
/// removing a pallet from the pick. Updates as more pallets are scanned on the Picking
/// List underneath (via [refresh]).
///
/// While this page is open it owns the scanner: scanning a pallet here doesn't pick it,
/// it offers to remove it from this line (confirmed by the picker, then [onDelete]).
class ProductPalletsScreen extends StatefulWidget {
  final PickItem line;
  final Listenable refresh;
  final List<ScannedPallet> Function() pallets;

  /// Removes a picked pallet (server + the Picking List's progress). Returns null when
  /// removed, otherwise a message to show.
  final Future<String?> Function(ScannedPallet) onDelete;

  const ProductPalletsScreen({
    super.key,
    required this.line,
    required this.refresh,
    required this.pallets,
    required this.onDelete,
  });

  @override
  State<ProductPalletsScreen> createState() => _ProductPalletsScreenState();
}

enum _RemoveStatus { removed, notOnLine, unreadable, error }

class _ProductPalletsScreenState extends State<ProductPalletsScreen> {
  static const Color _green = Color(0xFF16A34A);
  static const Color _amber = Color(0xFFD97706);

  // Scans arrive from the native DataWedge intent receiver (primary); the KeyboardListener
  // is the fallback for devices that emit keystrokes. Both route through _handleScan, which
  // drops an identical repeat within 800ms so the two can't double-fire.
  StreamSubscription<String>? _scanSub;
  String? _lastRaw;
  DateTime? _lastRawAt;
  bool _dialogOpen = false;
  bool _busy = false;

  final _scanFocus = FocusNode();
  final StringBuffer _scanBuf = StringBuffer();

  // Last removal outcome, shown on the right panel.
  _RemoveStatus? _status;
  String? _statusTitle;
  String? _statusSub;

  @override
  void initState() {
    super.initState();
    _scanSub = Scanner.instance.scans.listen(_handleScan);
  }

  @override
  void dispose() {
    _scanSub?.cancel();
    _scanFocus.dispose();
    super.dispose();
  }

  void _handleScan(String raw) {
    final text = raw.trim();
    if (text.isEmpty) return;
    final now = DateTime.now();
    if (_lastRaw == text &&
        _lastRawAt != null &&
        now.difference(_lastRawAt!) < const Duration(milliseconds: 800)) {
      return;
    }
    _lastRaw = text;
    _lastRawAt = now;

    // The confirm popup is up — ignore the scan but re-sound the error so the picker
    // answers it before scanning again.
    if (_dialogOpen || _busy) {
      Scanner.instance.playError();
      HapticFeedback.heavyImpact();
      return;
    }

    _processScan(text);
  }

  // Accumulate scanned characters; the trailing Enter ends a scan.
  void _onKey(KeyEvent e) {
    if (e is! KeyDownEvent) return;
    if (e.logicalKey == LogicalKeyboardKey.enter ||
        e.logicalKey == LogicalKeyboardKey.numpadEnter) {
      final raw = _scanBuf.toString().trim();
      _scanBuf.clear();
      if (raw.isNotEmpty) _handleScan(raw);
      return;
    }
    final ch = e.character;
    if (ch != null && ch.isNotEmpty) _scanBuf.write(ch);
  }

  void _processScan(String raw) {
    final uniqueCode = Scanner.field(raw, const ['UniqueCode:']);
    if (uniqueCode == null || uniqueCode.isEmpty) {
      _setStatus(_RemoveStatus.unreadable, 'Label not recognised', raw);
      return;
    }

    // Newest pick first, so a UniqueCode picked twice (the label printer duplicates codes)
    // removes the most recent — matching what the server deletes.
    final list = widget.pallets();
    ScannedPallet? match;
    for (var i = list.length - 1; i >= 0; i--) {
      if (!list[i].isNoScan && list[i].uniqueCode == uniqueCode) {
        match = list[i];
        break;
      }
    }
    if (match == null) {
      _setStatus(_RemoveStatus.notOnLine, 'Pallet not on this list',
          'UniqueCode $uniqueCode');
      return;
    }

    _confirmDelete(match);
  }

  void _setStatus(_RemoveStatus s, String? title, String? sub) {
    setState(() {
      _status = s;
      _statusTitle = title;
      _statusSub = sub;
    });
    if (s != _RemoveStatus.removed) {
      Scanner.instance.playError();
      HapticFeedback.heavyImpact();
    }
  }

  void _confirmDelete(ScannedPallet p) {
    _dialogOpen = true;
    showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        icon: const Icon(Icons.delete_outline, color: AppColors.error, size: 40),
        title: Text(p.isNoScan ? 'Remove this no-scan label?' : 'Remove this pallet?',
            textAlign: TextAlign.center,
            style: const TextStyle(
                fontFamily: 'Poppins', fontWeight: FontWeight.w700)),
        content: Text(
          '${p.isNoScan ? 'The no-scan label added at ${_clock(p.pickedAt)}' : 'Pallet ${p.uniqueCode}'}'
          ' will be removed from '
          '${widget.line.productDescription ?? 'this product'} '
          '(Order #${widget.line.orderNo}).',
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
            style: TextButton.styleFrom(foregroundColor: AppColors.error),
            child: const Text('Remove'),
          ),
        ],
      ),
    ).then((confirmed) {
      _dialogOpen = false;
      _focusScanner();
      if (confirmed == true) _delete(p);
    });
  }

  Future<void> _delete(ScannedPallet p) async {
    if (_busy) return;
    setState(() => _busy = true);
    try {
      final error = await widget.onDelete(p);
      if (!mounted) return;
      if (error == null) {
        _setStatus(
            _RemoveStatus.removed,
            p.isNoScan ? 'No-scan label removed' : 'Pallet removed',
            p.isNoScan ? 'Added at ${_clock(p.pickedAt)}' : 'UniqueCode ${p.uniqueCode}');
        HapticFeedback.selectionClick();
      } else {
        _setStatus(_RemoveStatus.error, 'Not removed', error);
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  // The newest pallet on this line that carries a code (a no-scan label has none).
  ScannedPallet? _lastScanned() {
    final list = widget.pallets();
    for (var i = list.length - 1; i >= 0; i--) {
      if (!list[i].isNoScan && list[i].uniqueCode.isNotEmpty) return list[i];
    }
    return null;
  }

  // Dev-only: replay the last picked pallet's label through the scan handler, so the
  // remove flow can be exercised on the emulator / at a desk with no scanner attached.
  void _simulateScan() {
    final p = _lastScanned();
    if (p == null) {
      _setStatus(_RemoveStatus.notOnLine, 'Nothing to simulate',
          'No scanned pallet on this line');
      return;
    }
    _lastRaw = null; // bypass the repeat guard so it can be pressed twice in a row
    _handleScan(
        'ProdCd:${p.productCode}Weight:0 kgUniqueCode:${p.uniqueCode}');
  }

  void _focusScanner() {
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) _scanFocus.requestFocus();
    });
  }

  @override
  Widget build(BuildContext context) {
    final landscape =
        MediaQuery.of(context).orientation == Orientation.landscape;
    return Scaffold(
      backgroundColor: AppColors.background,
      // No app bar — the item header panel carries the title and the Back button, and
      // the height goes to the picked pallets instead.
      body: SafeArea(
        child: KeyboardListener(
        focusNode: _scanFocus,
        autofocus: true,
        onKeyEvent: _onKey,
        child: AnimatedBuilder(
          animation: widget.refresh,
          builder: (context, _) {
            final list = widget.pallets();
            final picked = list.length;
            final target = widget.line.palletCount;
            final pct = target > 0 ? (picked / target).clamp(0.0, 1.0) : 0.0;
            final done = target > 0 && picked >= target;
            final accent = done ? _green : AppColors.primary;
            // Newest first. A line never exceeds its target (24 max), so split into three
            // columns — each filling top to bottom before the next starts — to show every
            // picked pallet on one screen.
            final ordered = [
              for (var i = list.length - 1; i >= 0; i--) list[i],
            ];
            const columns = 3;
            // Fill a column top to bottom before starting the next: 8 rows is a full
            // column at this panel's height, and 3 × 8 covers the 24-pallet maximum. Only
            // if a line somehow runs past that do the columns share the overflow evenly.
            const rowsPerColumn = 8;
            final even = (ordered.length / columns).ceil();
            final perColumn = even > rowsPerColumn ? even : rowsPerColumn;
            final chunks = [
              for (var i = 0; i < columns; i++)
                ordered.skip(i * perColumn).take(perColumn).toList(),
            ];
            final palletList = list.isEmpty
                ? _empty()
                // Scrolls only as a safety net; at <=24 items it always fits.
                : SingleChildScrollView(
                    child: IntrinsicHeight(
                      child: Row(
                        crossAxisAlignment: CrossAxisAlignment.stretch,
                        children: [
                          for (var i = 0; i < columns; i++) ...[
                            // A divider only where there's a filled column on each side —
                            // no stray rules hanging beside empty columns.
                            if (i > 0 && chunks[i].isNotEmpty)
                              const VerticalDivider(width: 1),
                            Expanded(child: _palletColumn(chunks[i])),
                          ],
                        ],
                      ),
                    ),
                  );
            return Stack(
              children: [
                Padding(
                  padding: const EdgeInsets.all(12),
                  // Landscape: the right column runs the full height — picked weight at
                  // the very top, remove-scan panel below — so the item header only spans
                  // the left column beside it.
                  child: landscape
                      ? Column(
                          crossAxisAlignment: CrossAxisAlignment.stretch,
                          children: [
                            // Top band: item header and picked weight side by side, the
                            // pair sized to the taller of the two so their edges line up.
                            IntrinsicHeight(
                              child: Row(
                                crossAxisAlignment: CrossAxisAlignment.stretch,
                                children: [
                                  Expanded(
                                    child: _header(
                                        picked, target, pct, done, accent),
                                  ),
                                  const SizedBox(width: 12),
                                  SizedBox(width: 280, child: _weightPanel(list)),
                                ],
                              ),
                            ),
                            const SizedBox(height: 12),
                            Expanded(
                              child: Row(
                                crossAxisAlignment: CrossAxisAlignment.stretch,
                                children: [
                                  Expanded(child: _panel(palletList)),
                                  const SizedBox(width: 12),
                                  SizedBox(
                                      width: 280,
                                      child: _scanPanel(vertical: true)),
                                ],
                              ),
                            ),
                          ],
                        )
                      : Column(
                          crossAxisAlignment: CrossAxisAlignment.stretch,
                          children: [
                            _header(picked, target, pct, done, accent),
                            const SizedBox(height: 12),
                            Expanded(child: _panel(palletList)),
                            const SizedBox(height: 12),
                            _weightPanel(list),
                            const SizedBox(height: 12),
                            SizedBox(height: 132, child: _scanPanel()),
                          ],
                        ),
                ),
                if (kDebugMode)
                  Positioned(right: 24, bottom: 24, child: _devScanButton()),
              ],
            );
          },
        ),
        ),
      ),
    );
  }

  // The page's panels: white, rounded, hairline border — same shape as the Picking List.
  Widget _panel(Widget child) => Container(
        decoration: BoxDecoration(
          color: AppColors.surface,
          borderRadius: BorderRadius.circular(12),
          border: Border.all(color: AppColors.borderLight),
        ),
        clipBehavior: Clip.antiAlias,
        child: child,
      );

  // Picked weight for this order item — the pallet weights picked onto this line added
  // up. A no-scan label (or a pallet the label didn't match) carries the server's estimate
  // from the product's other pallets, so those are flagged: the figure is close, not
  // measured.
  Widget _weightPanel(List<ScannedPallet> pallets) {
    final weight = pallets.fold<double>(0, (s, p) => s + p.weight);
    final estimated = pallets.where((p) => p.isWeightEstimated).length;
    return _panel(
      Padding(
        padding: const EdgeInsets.fromLTRB(16, 10, 16, 12),
        child: Column(
          // Right-aligned and vertically centred — the panel is stretched to the item
          // header's height, so the figure sits in the middle of it.
          crossAxisAlignment: CrossAxisAlignment.end,
          mainAxisAlignment: MainAxisAlignment.center,
          mainAxisSize: MainAxisSize.min,
          children: [
            const Text(
              'Weight Picked (Calc)',
              textAlign: TextAlign.right,
              style: TextStyle(
                fontFamily: 'Poppins',
                fontSize: 13,
                fontWeight: FontWeight.w500,
                color: AppColors.textLight,
              ),
            ),
            const SizedBox(height: 2),
            Text(
              '${fmtKg(weight)} Kg',
              maxLines: 1,
              softWrap: false,
              textAlign: TextAlign.right,
              overflow: TextOverflow.ellipsis,
              style: const TextStyle(
                fontFamily: 'Poppins',
                fontSize: 24,
                fontWeight: FontWeight.w700,
                color: AppColors.textDark,
              ),
            ),
            if (estimated > 0) ...[
              const SizedBox(height: 4),
              Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  const Icon(Icons.flag, size: 15, color: _amber),
                  const SizedBox(width: 5),
                  Flexible(
                    child: Text(
                      'incl. $estimated estimated',
                      maxLines: 1,
                      softWrap: false,
                      overflow: TextOverflow.ellipsis,
                      style: const TextStyle(
                        fontFamily: 'Poppins',
                        fontSize: 13,
                        fontWeight: FontWeight.w700,
                        color: _amber,
                      ),
                    ),
                  ),
                ],
              ),
            ],
          ],
        ),
      ),
    );
  }

  // Back, sized for a gloved finger now that it carries the app bar's job.
  Widget _backButton() => IconButton(
        icon: const Icon(Icons.arrow_back, size: 24),
        tooltip: 'Back',
        onPressed: () => Navigator.of(context).maybePop(),
        color: AppColors.textDark,
        constraints: const BoxConstraints.tightFor(width: 42, height: 42),
        padding: EdgeInsets.zero,
        splashRadius: 24,
      );

  // The order item, in its own rounded panel across the top: Back, the pallet-count tile,
  // product + order, and the picked/target count, over a progress bar.
  Widget _header(int picked, int target, double pct, bool done, Color accent) {
    return _panel(
      Padding(
      padding: const EdgeInsets.fromLTRB(6, 10, 16, 14),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              _backButton(),
              const SizedBox(width: 6),
              Container(
                width: 52,
                height: 52,
                alignment: Alignment.center,
                decoration: BoxDecoration(
                  color: accent.withValues(alpha: 0.12),
                  borderRadius: BorderRadius.circular(14),
                ),
                child: done
                    ? Icon(Icons.check, color: _green, size: 28)
                    : Text(
                        '$target',
                        style: const TextStyle(
                          fontFamily: 'Poppins',
                          fontSize: 20,
                          fontWeight: FontWeight.w700,
                          color: AppColors.primary,
                        ),
                      ),
              ),
              const SizedBox(width: 14),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      widget.line.productDescription ??
                          'Product ${widget.line.productId}',
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                      style: const TextStyle(
                        fontFamily: 'Poppins',
                        fontSize: 18,
                        fontWeight: FontWeight.w700,
                        color: AppColors.textDark,
                        height: 1.2,
                      ),
                    ),
                    const SizedBox(height: 3),
                    Text(
                      'Order #${widget.line.orderNo}',
                      style: const TextStyle(
                        fontFamily: 'Poppins',
                        fontSize: 13,
                        fontWeight: FontWeight.w500,
                        color: AppColors.textLight,
                      ),
                    ),
                  ],
                ),
              ),
              Text(
                '$picked / $target',
                style: TextStyle(
                  fontFamily: 'Poppins',
                  fontSize: 18,
                  fontWeight: FontWeight.w700,
                  color: done ? _green : AppColors.textDark,
                ),
              ),
            ],
          ),
          const SizedBox(height: 12),
          ClipRRect(
            borderRadius: BorderRadius.circular(6),
            child: LinearProgressIndicator(
              value: pct,
              minHeight: 8,
              backgroundColor: AppColors.borderLight,
              valueColor: AlwaysStoppedAnimation(accent),
            ),
          ),
        ],
      ),
      ),
    );
  }

  // One column of picked-pallet rows (dividers between), sized to its content.
  Widget _palletColumn(List<ScannedPallet> items) {
    final children = <Widget>[];
    for (var i = 0; i < items.length; i++) {
      if (i > 0) children.add(const Divider(height: 1));
      children.add(_palletRow(items[i]));
    }
    return Column(
      mainAxisSize: MainAxisSize.min,
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: children,
    );
  }

  // A no-scan label has no barcode to re-scan, so it's removed by tapping it instead —
  // the same confirm popup, reached the only way it can be.
  Widget _palletRow(ScannedPallet p) {
    final noScan = p.isNoScan;
    final row = _palletRowBody(p);
    if (!noScan) return row;
    return Material(
      color: Colors.transparent,
      child: InkWell(
        onTap: _busy || _dialogOpen ? null : () => _confirmDelete(p),
        child: row,
      ),
    );
  }

  Widget _palletRowBody(ScannedPallet p) {
    final noScan = p.isNoScan;
    return Container(
      color: AppColors.surface,
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 9),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.center,
        children: [
          Icon(noScan ? Icons.edit_note : Icons.check_circle,
              color: noScan ? _amber : _green, size: 22),
          const SizedBox(width: 10),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  noScan ? 'No-scan label' : p.uniqueCode,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(
                    fontFamily: 'Poppins',
                    fontSize: 14,
                    fontWeight: FontWeight.w700,
                    fontStyle: noScan ? FontStyle.italic : FontStyle.normal,
                    color: noScan ? _amber : AppColors.textDark,
                  ),
                ),
                const SizedBox(height: 1),
                Text(
                  noScan
                      ? 'Added manually  ·  ${_clock(p.pickedAt)}  ·  tap to remove'
                      : '${p.productCode}  ·  ${_clock(p.pickedAt)}',
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: const TextStyle(
                    fontFamily: 'Poppins',
                    fontSize: 12,
                    fontWeight: FontWeight.w500,
                    color: AppColors.textLight,
                  ),
                ),
              ],
            ),
          ),
          if (noScan) ...[
            const SizedBox(width: 6),
            const Icon(Icons.delete_outline, color: _amber, size: 20),
          ],
        ],
      ),
    );
  }

  // Debug builds only — the lavender dev styling used across the apps marks it as a
  // developer control, never shipped to a release build.
  Widget _devScanButton() {
    return OutlinedButton.icon(
      onPressed: _busy || _dialogOpen ? null : _simulateScan,
      icon: const Icon(Icons.qr_code_scanner, size: 16),
      label: const Text('Test scan'),
      style: OutlinedButton.styleFrom(
        foregroundColor: const Color(0xFF7C3AED),
        backgroundColor: const Color(0xFFF3E8FF),
        side: const BorderSide(color: Color(0xFF7C3AED)),
        padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
        shape:
            RoundedRectangleBorder(borderRadius: BorderRadius.circular(10)),
        textStyle: const TextStyle(
          fontFamily: 'Poppins',
          fontSize: 12,
          fontWeight: FontWeight.w600,
        ),
      ),
    );
  }

  // The remove-a-pallet scan panel — same shape as the Picking List's scan panel, in
  // red so it reads as "scanning here deletes".
  Widget _scanPanel({bool vertical = false}) {
    return _panel(
      _busy
          ? const Center(
              child: SizedBox(
                width: 28,
                height: 28,
                child: CircularProgressIndicator(strokeWidth: 3),
              ),
            )
          : vertical
              ? (_status == null ? _scanPromptVertical() : _scanResultVertical())
              : (_status == null ? _scanPrompt() : _scanResult()),
    );
  }

  Widget _scanPromptVertical() {
    return Padding(
      padding: const EdgeInsets.all(20),
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          Container(
            width: 84,
            height: 84,
            decoration: BoxDecoration(
              color: AppColors.error.withValues(alpha: 0.12),
              borderRadius: BorderRadius.circular(22),
            ),
            child:
                const Icon(Icons.qr_code_scanner, color: AppColors.error, size: 44),
          ),
          const SizedBox(height: 18),
          const Text(
            'Scan a pallet',
            textAlign: TextAlign.center,
            style: TextStyle(
              fontFamily: 'Poppins',
              fontSize: 19,
              fontWeight: FontWeight.w700,
              color: AppColors.textDark,
            ),
          ),
          const SizedBox(height: 6),
          const Text(
            'Scan a pallet again to remove it from this pick',
            textAlign: TextAlign.center,
            style: TextStyle(
              fontFamily: 'Poppins',
              fontSize: 14,
              fontWeight: FontWeight.w500,
              color: AppColors.textLight,
            ),
          ),
          if (_hasNoScan) ...[
            const SizedBox(height: 10),
            const Text(
              'A no-scan label has no barcode — tap it in the list to remove it',
              textAlign: TextAlign.center,
              style: TextStyle(
                fontFamily: 'Poppins',
                fontSize: 12.5,
                fontWeight: FontWeight.w500,
                color: _amber,
              ),
            ),
          ],
        ],
      ),
    );
  }

  bool get _hasNoScan => widget.pallets().any((p) => p.isNoScan);

  Widget _scanResultVertical() {
    final ok = _status == _RemoveStatus.removed;
    final color = ok ? _green : AppColors.error;
    return Padding(
      padding: const EdgeInsets.all(20),
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          Icon(ok ? Icons.delete_forever : Icons.error, color: color, size: 72),
          const SizedBox(height: 16),
          Text(
            _statusTitle ?? '',
            textAlign: TextAlign.center,
            maxLines: 3,
            overflow: TextOverflow.ellipsis,
            style: TextStyle(
              fontFamily: 'Poppins',
              fontSize: 18,
              fontWeight: FontWeight.w700,
              color: ok ? AppColors.textDark : color,
              height: 1.25,
            ),
          ),
          if (_statusSub != null) ...[
            const SizedBox(height: 6),
            Text(
              _statusSub!,
              textAlign: TextAlign.center,
              maxLines: 2,
              overflow: TextOverflow.ellipsis,
              style: const TextStyle(
                fontFamily: 'Poppins',
                fontSize: 13.5,
                fontWeight: FontWeight.w500,
                color: AppColors.textLight,
              ),
            ),
          ],
          const SizedBox(height: 16),
          Text(
            'Scan another pallet to remove it',
            textAlign: TextAlign.center,
            style: TextStyle(
              fontFamily: 'Poppins',
              fontSize: 12.5,
              fontWeight: FontWeight.w500,
              color: AppColors.textMuted,
            ),
          ),
        ],
      ),
    );
  }

  Widget _scanPrompt() {
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 16),
      child: Row(
        children: [
          Container(
            width: 60,
            height: 60,
            decoration: BoxDecoration(
              color: AppColors.error.withValues(alpha: 0.12),
              borderRadius: BorderRadius.circular(16),
            ),
            child:
                const Icon(Icons.qr_code_scanner, color: AppColors.error, size: 32),
          ),
          const SizedBox(width: 16),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                const Text(
                  'Scan a pallet',
                  style: TextStyle(
                    fontFamily: 'Poppins',
                    fontSize: 18,
                    fontWeight: FontWeight.w700,
                    color: AppColors.textDark,
                  ),
                ),
                const SizedBox(height: 4),
                const Text(
                  'Scan a pallet again to remove it from this pick',
                  style: TextStyle(
                    fontFamily: 'Poppins',
                    fontSize: 14,
                    fontWeight: FontWeight.w500,
                    color: AppColors.textLight,
                  ),
                ),
                if (_hasNoScan) ...[
                  const SizedBox(height: 4),
                  const Text(
                    'Tap a no-scan label in the list to remove it',
                    style: TextStyle(
                      fontFamily: 'Poppins',
                      fontSize: 12.5,
                      fontWeight: FontWeight.w500,
                      color: _amber,
                    ),
                  ),
                ],
              ],
            ),
          ),
        ],
      ),
    );
  }

  Widget _scanResult() {
    final ok = _status == _RemoveStatus.removed;
    final color = ok ? _green : AppColors.error;
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 14),
      child: Row(
        children: [
          Icon(ok ? Icons.delete_forever : Icons.error, color: color, size: 52),
          const SizedBox(width: 16),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                Text(
                  _statusTitle ?? '',
                  maxLines: 2,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(
                    fontFamily: 'Poppins',
                    fontSize: 18,
                    fontWeight: FontWeight.w700,
                    color: ok ? AppColors.textDark : color,
                    height: 1.2,
                  ),
                ),
                if (_statusSub != null) ...[
                  const SizedBox(height: 3),
                  Text(
                    _statusSub!,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: const TextStyle(
                      fontFamily: 'Poppins',
                      fontSize: 13.5,
                      fontWeight: FontWeight.w500,
                      color: AppColors.textLight,
                    ),
                  ),
                ],
              ],
            ),
          ),
        ],
      ),
    );
  }

  // HH:mm:ss — pallets are picked within one session, so the time alone identifies a scan.
  static String _clock(DateTime t) {
    String two(int n) => n.toString().padLeft(2, '0');
    return '${two(t.hour)}:${two(t.minute)}:${two(t.second)}';
  }

  Widget _empty() => Center(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Text(
            'No pallets scanned yet',
            style: TextStyle(
              fontFamily: 'Poppins',
              fontSize: 14,
              color: AppColors.textLight,
            ),
          ),
        ),
      );
}
