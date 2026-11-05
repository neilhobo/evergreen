// ignore_for_file: avoid_print
//
// Spawns `flutter run --machine -d <device>` and exposes a tiny HTTP server
// on http://127.0.0.1:8991 for triggering hot reload / hot restart over HTTP:
//
//   POST /reload   -> app.restart  (fullRestart=false)
//   POST /restart  -> app.restart  (fullRestart=true)
//   POST /quit     -> app.stop
//   GET  /status   -> { ready, appId }
//
// This lets an outside process (CI, this assistant, etc.) push hot reloads
// without keystroke access to the running flutter terminal.

import 'dart:async';
import 'dart:convert';
import 'dart:io';

Future<void> main(List<String> args) async {
  final dashDash = args.indexOf('--');
  final scriptArgs = dashDash >= 0 ? args.sublist(0, dashDash) : args;
  final extraFlutterArgs =
      dashDash >= 0 ? args.sublist(dashDash + 1) : const <String>[];

  final device = _argValue(scriptArgs, '--device') ?? 'emulator-5554';
  final port = int.tryParse(_argValue(scriptArgs, '--port') ?? '') ?? 8991;
  final flutterBat = Platform.isWindows ? 'C:\\flutter\\bin\\flutter.bat' : 'flutter';

  print('==> spawning flutter run --machine -d $device ${extraFlutterArgs.join(' ')}');

  final proc = await Process.start(
    flutterBat,
    ['run', '--machine', '-d', device, ...extraFlutterArgs],
    runInShell: true,
    workingDirectory: Directory.current.path,
  );

  String? appId;
  final readyCompleter = Completer<void>();
  int nextId = 1;
  final pending = <int, Completer<dynamic>>{};

  proc.stdout
      .transform(utf8.decoder)
      .transform(const LineSplitter())
      .listen((line) {
    // Daemon framing: each line is `[{...}]`. Anything else is plain log output.
    if (!(line.startsWith('[{') && line.endsWith('}]'))) {
      stdout.writeln('[flutter] $line');
      return;
    }
    final List<dynamic> msgs;
    try {
      msgs = jsonDecode(line) as List<dynamic>;
    } catch (e) {
      stdout.writeln('[flutter] $line');
      return;
    }
    for (final msg in msgs) {
      final m = msg as Map<String, dynamic>;
      if (m.containsKey('event')) {
        final event = m['event'] as String;
        final params = m['params'] as Map<String, dynamic>?;
        if (event == 'app.started' && params != null) {
          appId = params['appId'] as String?;
          if (!readyCompleter.isCompleted) readyCompleter.complete();
          stdout.writeln('==> app started: appId=$appId');
        } else if (event == 'app.progress') {
          final progress = params?['message'] ?? params?['id'];
          if (progress != null) stdout.writeln('==> $progress');
        } else if (event == 'app.log') {
          final log = params?['log'];
          if (log != null) stdout.writeln('[app] $log');
        } else if (event == 'app.stop') {
          stdout.writeln('==> app stopped');
        } else {
          stdout.writeln('[event:$event] ${jsonEncode(params ?? {})}');
        }
      } else if (m.containsKey('id')) {
        final id = m['id'] as int;
        final completer = pending.remove(id);
        if (completer != null) {
          if (m.containsKey('error')) {
            completer.completeError(m['error']);
          } else {
            completer.complete(m['result']);
          }
        }
      }
    }
  });

  proc.stderr
      .transform(utf8.decoder)
      .transform(const LineSplitter())
      .listen((line) => stderr.writeln('[flutter:err] $line'));

  unawaited(proc.exitCode.then((code) {
    stdout.writeln('==> flutter exited with code $code');
    exit(code);
  }));

  Future<dynamic> daemonCall(String method, Map<String, dynamic> params) {
    final id = nextId++;
    final completer = Completer<dynamic>();
    pending[id] = completer;
    final cmd = jsonEncode([
      {'id': id, 'method': method, 'params': params}
    ]);
    proc.stdin.writeln(cmd);
    return completer.future.timeout(const Duration(seconds: 30));
  }

  final server = await HttpServer.bind('127.0.0.1', port);
  stdout.writeln('==> control server listening on http://127.0.0.1:$port');
  stdout.writeln('    POST /reload | POST /restart | POST /quit | GET /status');

  // Watch lib/ for any .dart file change and auto-trigger a hot reload. Saves
  // in VS Code (or any editor) get reflected on the device without us pressing
  // anything. Debounced to coalesce burst writes from formatters etc.
  Timer? watchDebounce;
  Future<void> autoReload() async {
    if (appId == null) return;
    try {
      await daemonCall('app.restart', {
        'appId': appId!,
        'fullRestart': false,
        'pause': false,
        'reason': 'auto-watch',
      });
      stdout.writeln('==> auto hot reload (file change)');
    } catch (e) {
      stdout.writeln('==> auto hot reload failed: $e');
    }
  }

  final libDir = Directory('${Directory.current.path}/lib');
  if (libDir.existsSync()) {
    libDir.watch(recursive: true).listen((event) {
      if (!event.path.endsWith('.dart')) return;
      watchDebounce?.cancel();
      watchDebounce = Timer(const Duration(milliseconds: 350), autoReload);
    });
    stdout.writeln('==> watching ${libDir.path} for .dart changes (auto reload)');
  }

  // Typed in the terminal (then Enter): r = hot reload, R = hot restart, as plain
  // `flutter run` offers. Under npm run dev:full the keys arrive line by line.
  stdin.transform(utf8.decoder).transform(const LineSplitter()).listen((line) async {
    final key = line.trim();
    if (key != 'r' && key != 'R') return;
    if (appId == null) {
      stdout.writeln('==> app not ready yet');
      return;
    }
    try {
      await daemonCall('app.restart', {
        'appId': appId!,
        'fullRestart': key == 'R',
        'pause': false,
        'reason': 'keyboard',
      });
      stdout.writeln(key == 'R' ? '==> hot restart' : '==> hot reload');
    } catch (e) {
      stdout.writeln('==> ${key == 'R' ? 'hot restart' : 'hot reload'} failed: $e');
    }
  }, onError: (_) {});
  stdout.writeln('    type r + Enter = hot reload, R + Enter = hot restart');

  await for (final req in server) {
    final res = req.response;
    res.headers.contentType = ContentType.text;
    final path = req.uri.path;
    try {
      if (path == '/status') {
        res.headers.contentType = ContentType.json;
        res.write(jsonEncode({'ready': appId != null, 'appId': appId}));
      } else if (path == '/reload' || path == '/restart') {
        if (appId == null) {
          res.statusCode = 503;
          res.write('App not yet started\n');
        } else {
          final fullRestart = path == '/restart';
          final result = await daemonCall('app.restart', {
            'appId': appId!,
            'fullRestart': fullRestart,
            'pause': false,
            'reason': 'manual',
          });
          final ok = result is Map && result['code'] == 0;
          res.statusCode = ok ? 200 : 500;
          res.write('${fullRestart ? 'Restart' : 'Reload'}: ${jsonEncode(result)}\n');
        }
      } else if (path == '/quit') {
        if (appId != null) await daemonCall('app.stop', {'appId': appId!});
        res.write('Stopping…\n');
      } else {
        res.statusCode = 404;
        res.write('Unknown path: $path\n');
      }
    } catch (e) {
      res.statusCode = 500;
      res.write('Error: $e\n');
    } finally {
      await res.close();
    }
  }
}

String? _argValue(List<String> args, String flag) {
  for (var i = 0; i < args.length; i++) {
    if (args[i] == flag && i + 1 < args.length) return args[i + 1];
    if (args[i].startsWith('$flag=')) return args[i].substring(flag.length + 1);
  }
  return null;
}
