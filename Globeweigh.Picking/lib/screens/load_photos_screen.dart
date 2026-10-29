import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_image_compress/flutter_image_compress.dart';

import '../api/api_client.dart';
import '../theme/app_colors.dart';
import 'load_weight_screen.dart';
import 'take_photo_screen.dart';

/// The photos required to complete a load, in order. Edit this list to change
/// how many are required or what each one is called. A load with a POS board on any of
/// its orders needs [kPosPhotoDescription] as well (see [LoadPhotosScreen.hasPos]).
const List<String> kLoadPhotoDescriptions = [
  'Trailer Number',
  'Side of Trailer',
  'Other Side of Trailer',
];
const String kPosPhotoDescription = 'POS Boards';

/// Complete Load — reached from the Pending Loads COMPLETE button. The user must
/// take one photo per entry in [kLoadPhotoDescriptions] (plus POS Boards when [hasPos]) before the load can be
/// completed; the green button stays disabled until every slot is filled. On
/// Complete the photos are uploaded to the server (stored against the load).
class LoadPhotosScreen extends StatefulWidget {
  final int recordId;
  final String? haulierName;

  /// Some order on the load has a POS board: a 4th photo, POS Boards, is required.
  final bool hasPos;

  const LoadPhotosScreen({
    super.key,
    required this.recordId,
    this.haulierName,
    this.hasPos = false,
  });

  @override
  State<LoadPhotosScreen> createState() => _LoadPhotosScreenState();
}

class _LoadPhotosScreenState extends State<LoadPhotosScreen> {
  final _api = ApiClient();
  // The photos this load needs: the standard set, plus POS Boards when it carries one.
  late final List<String> _descriptions = [
    ...kLoadPhotoDescriptions,
    if (widget.hasPos) kPosPhotoDescription,
  ];
  // Compressed JPEG bytes per required photo, in _descriptions order.
  late final List<Uint8List?> _photos =
      List<Uint8List?>.filled(_descriptions.length, null);
  bool _capturing = false;
  bool _saving = false;

  static const Color _green = Color(0xFF16A34A);

  bool get _allTaken => _photos.every((b) => b != null);

  Future<void> _takePhoto(int index) async {
    if (_capturing || _saving) return;
    _capturing = true;
    try {
      // In-app camera with an explicit Cancel — pops with null when backed out.
      final path = await Navigator.of(context).push<String>(
        MaterialPageRoute(
          builder: (_) => TakePhotoScreen(title: _descriptions[index]),
        ),
      );
      if (path == null || !mounted) return;

      // Compress to ~1080px JPEG so uploads + storage stay small and the output
      // type is predictable (the API stores it as .jpg) — same as cirrus-mobile.
      final bytes = await FlutterImageCompress.compressWithFile(
        path,
        minWidth: 1080,
        minHeight: 1080,
        quality: 80,
        format: CompressFormat.jpeg,
      );
      if (!mounted) return;
      if (bytes == null) {
        _snack('Could not read photo');
        return;
      }
      setState(() => _photos[index] = bytes);
    } finally {
      _capturing = false;
    }
  }

  // Next: upload the photos, then move on to the Capture Weight step. If the weight
  // is captured there (load completed), pop back to Pending Loads with success.
  Future<void> _next() async {
    if (!_allTaken || _saving) return;
    setState(() => _saving = true);
    try {
      for (var i = 0; i < _descriptions.length; i++) {
        await _api.uploadLoadPhoto(
          recordId: widget.recordId,
          description: _descriptions[i],
          bytes: _photos[i]!,
        );
      }
      if (!mounted) return;
      setState(() => _saving = false);
      final completed = await Navigator.of(context).push<bool>(
        MaterialPageRoute(
          builder: (_) => LoadWeightScreen(
            recordId: widget.recordId,
            haulierName: widget.haulierName,
          ),
        ),
      );
      if (completed == true && mounted) Navigator.of(context).pop(true);
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() => _saving = false);
      _snack(e.message);
    }
  }

  void _snack(String message) {
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(message,
            style: const TextStyle(fontFamily: 'Poppins', fontSize: 14)),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final landscape =
        MediaQuery.of(context).orientation == Orientation.landscape;
    return Scaffold(
      backgroundColor: AppColors.background,
      appBar: AppBar(
        backgroundColor: AppColors.navy,
        foregroundColor: Colors.white,
        surfaceTintColor: Colors.transparent,
        title: Text(
          widget.haulierName == null
              ? 'Complete Load'
              : 'Complete Load — ${widget.haulierName}',
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          style: const TextStyle(
            fontFamily: 'Poppins',
            fontWeight: FontWeight.w700,
            color: Colors.white,
          ),
        ),
      ),
      body: Stack(
        children: [
          Padding(
            padding: const EdgeInsets.all(12),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Text(
                  'Take all ${_descriptions.length} photos to complete this load',
                  textAlign: TextAlign.center,
                  style: const TextStyle(
                    fontFamily: 'Poppins',
                    fontSize: 14,
                    fontWeight: FontWeight.w600,
                    color: AppColors.textLight,
                  ),
                ),
                const SizedBox(height: 12),
                // Landscape: the three slots side by side. Portrait: stacked.
                Expanded(
                  child: landscape
                      ? Row(
                          crossAxisAlignment: CrossAxisAlignment.stretch,
                          children: [
                            for (var i = 0;
                                i < _descriptions.length;
                                i++) ...[
                              if (i > 0) const SizedBox(width: 12),
                              Expanded(child: _photoCard(i)),
                            ],
                          ],
                        )
                      : Column(
                          crossAxisAlignment: CrossAxisAlignment.stretch,
                          children: [
                            for (var i = 0;
                                i < _descriptions.length;
                                i++) ...[
                              if (i > 0) const SizedBox(height: 12),
                              Expanded(child: _photoCard(i)),
                            ],
                          ],
                        ),
                ),
                const SizedBox(height: 12),
                Align(
                  alignment: Alignment.centerRight,
                  child: ElevatedButton.icon(
                    icon: const Icon(Icons.arrow_forward, size: 22),
                    label: const Text('Next'),
                    style: ElevatedButton.styleFrom(
                      backgroundColor: AppColors.primary,
                      foregroundColor: Colors.white,
                      disabledBackgroundColor: AppColors.borderLight,
                      disabledForegroundColor: AppColors.textMuted,
                      minimumSize: const Size(180, 52),
                      textStyle: const TextStyle(
                        fontFamily: 'Poppins',
                        fontSize: 16,
                        fontWeight: FontWeight.w700,
                      ),
                    ),
                    onPressed: _allTaken && !_saving ? _next : null,
                  ),
                ),
              ],
            ),
          ),
          // Uploading overlay — blocks input while the photos are sent.
          if (_saving)
            Positioned.fill(
              child: ColoredBox(
                color: Colors.black.withValues(alpha: 0.25),
                child: const Center(child: CircularProgressIndicator()),
              ),
            ),
        ],
      ),
    );
  }

  /// One required photo slot — the description on top, then either the camera
  /// prompt or the captured photo with a retake button.
  Widget _photoCard(int index) {
    final photo = _photos[index];
    final taken = photo != null;
    return Container(
      decoration: BoxDecoration(
        color: AppColors.surface,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(
          color: taken ? _green : AppColors.borderLight,
          width: taken ? 1.5 : 1,
        ),
      ),
      clipBehavior: Clip.antiAlias,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
            child: Row(
              children: [
                Icon(
                  taken ? Icons.check_circle : Icons.photo_camera_outlined,
                  size: 20,
                  color: taken ? _green : AppColors.textLight,
                ),
                const SizedBox(width: 8),
                Expanded(
                  child: Text(
                    _descriptions[index],
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: const TextStyle(
                      fontFamily: 'Poppins',
                      fontSize: 14.5,
                      fontWeight: FontWeight.w700,
                      color: AppColors.textDark,
                    ),
                  ),
                ),
              ],
            ),
          ),
          const Divider(height: 1),
          Expanded(
            child: taken
                ? Stack(
                    fit: StackFit.expand,
                    children: [
                      Image.memory(photo, fit: BoxFit.cover),
                      Positioned(
                        right: 8,
                        bottom: 8,
                        child: ElevatedButton.icon(
                          icon: const Icon(Icons.refresh, size: 18),
                          label: const Text('Retake'),
                          style: ElevatedButton.styleFrom(
                            backgroundColor:
                                Colors.black.withValues(alpha: 0.55),
                            foregroundColor: Colors.white,
                            padding: const EdgeInsets.symmetric(
                                horizontal: 14, vertical: 8),
                            textStyle: const TextStyle(
                              fontFamily: 'Poppins',
                              fontSize: 13,
                              fontWeight: FontWeight.w600,
                            ),
                          ),
                          onPressed: () => _takePhoto(index),
                        ),
                      ),
                    ],
                  )
                : Material(
                    color: Colors.transparent,
                    child: InkWell(
                      onTap: () => _takePhoto(index),
                      child: Column(
                        mainAxisAlignment: MainAxisAlignment.center,
                        children: [
                          Container(
                            width: 64,
                            height: 64,
                            decoration: BoxDecoration(
                              color: AppColors.primary.withValues(alpha: 0.12),
                              borderRadius: BorderRadius.circular(18),
                            ),
                            child: const Icon(Icons.photo_camera,
                                color: AppColors.primary, size: 32),
                          ),
                          const SizedBox(height: 12),
                          const Text(
                            'Tap to take photo',
                            style: TextStyle(
                              fontFamily: 'Poppins',
                              fontSize: 13.5,
                              fontWeight: FontWeight.w600,
                              color: AppColors.textLight,
                            ),
                          ),
                        ],
                      ),
                    ),
                  ),
          ),
        ],
      ),
    );
  }
}
