import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../data/pos_api.dart';
import '../auth/auth_controller.dart';

final posApiProvider = Provider<PosApi>((ref) => PosApi(ref.watch(apiClientProvider)));

/// The signed-in user's Staff tags, asked of the server at each sign-in and app
/// start: a tag given in Admin > Staff since the phone signed in still lands.
final myTagsProvider = FutureProvider<List<String>>((ref) async {
  if (ref.watch(signedInUserIdProvider) == null) return const [];
  return ref.watch(posApiProvider).myTags();
});

/// The open orders still owed a POS board.
final posOrdersProvider = FutureProvider<List<PosOrder>>((ref) async {
  ref.watch(signedInUserIdProvider);
  return ref.watch(posApiProvider).orders();
});
