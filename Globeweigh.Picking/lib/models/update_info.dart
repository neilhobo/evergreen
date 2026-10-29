/// The pending-update info returned by /api/devices/register.
class UpdateInfo {
  final bool pendingUpdate;
  final String? pendingVersion;
  final String? pendingPackageFileName;

  const UpdateInfo({
    this.pendingUpdate = false,
    this.pendingVersion,
    this.pendingPackageFileName,
  });

  factory UpdateInfo.fromJson(Map<String, dynamic> json) => UpdateInfo(
        pendingUpdate: json['pendingUpdate'] as bool? ?? false,
        pendingVersion: json['pendingVersion'] as String?,
        pendingPackageFileName: json['pendingPackageFileName'] as String?,
      );
}
