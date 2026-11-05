import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'customers_screen.dart';
import 'my_orders_screen.dart';
import 'sales_format.dart';
import 'sales_providers.dart';

/// The Sales role's app: My Orders, Customers and Completed along the foot
/// (design A, "Ledger list"), as TauriAdmin's SALES_NAV gives a rep.
class SalesShell extends ConsumerStatefulWidget {
  const SalesShell({super.key});

  @override
  ConsumerState<SalesShell> createState() => _SalesShellState();
}

class _SalesShellState extends ConsumerState<SalesShell> with WidgetsBindingObserver {
  int _tab = 0;
  Timer? _poll;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    // The office moves orders on (approves, returns, loads, dispatches): keep
    // the list current, as TauriAdmin does every minute.
    _poll = Timer.periodic(const Duration(seconds: 60), (_) => ref.invalidate(salesOrdersProvider));
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) ref.invalidate(salesOrdersProvider);
  }

  @override
  void dispose() {
    _poll?.cancel();
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: const Color(0xFFF4F6F9),
      body: IndexedStack(
        index: _tab,
        children: const [MyOrdersScreen(), CustomersScreen(), MyOrdersScreen(completed: true)],
      ),
      bottomNavigationBar: NavigationBarTheme(
        data: NavigationBarThemeData(
          backgroundColor: Colors.white,
          indicatorColor: SalesHeader.navy.withValues(alpha: 0.10),
          iconTheme: WidgetStateProperty.resolveWith(
            (s) => IconThemeData(color: s.contains(WidgetState.selected) ? SalesHeader.navy : const Color(0xFF64748B)),
          ),
          labelTextStyle: WidgetStateProperty.resolveWith(
            (s) => TextStyle(
              fontFamily: 'Poppins',
              fontSize: 11,
              fontWeight: s.contains(WidgetState.selected) ? FontWeight.w600 : FontWeight.w500,
              color: s.contains(WidgetState.selected) ? SalesHeader.navy : const Color(0xFF64748B),
            ),
          ),
        ),
        child: DecoratedBox(
          decoration: const BoxDecoration(
            border: Border(top: BorderSide(color: Color(0xFFE5E9EF))),
          ),
          child: NavigationBar(
            height: 66,
            selectedIndex: _tab,
            onDestinationSelected: (i) => setState(() => _tab = i),
            destinations: const [
              NavigationDestination(
                icon: Icon(Icons.description_outlined),
                selectedIcon: Icon(Icons.description),
                label: 'My Orders',
              ),
              NavigationDestination(
                icon: Icon(Icons.groups_outlined),
                selectedIcon: Icon(Icons.groups),
                label: 'Customers',
              ),
              NavigationDestination(
                icon: Icon(Icons.task_alt_outlined),
                selectedIcon: Icon(Icons.task_alt),
                label: 'Completed',
              ),
            ],
          ),
        ),
      ),
    );
  }
}
