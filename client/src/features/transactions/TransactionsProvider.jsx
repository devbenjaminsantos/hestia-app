import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  createTransaction,
  deleteInstallmentGroup,
  deleteTransaction,
  getInstallmentPlans,
  getRecurringRules,
  getTransactions,
  importTransactions,
  updateInstallmentGroup,
  updateTransaction,
} from "../../lib/api/transactions";
import { hasValidSession } from "../../lib/api/auth";
import { currentDayISO, getPostedTransactions, summarizeTransactions } from "../../lib/transactions/transactionSummary";
import { TransactionsContext } from "./TransactionsContext";

export function TransactionsProvider({ children }) {
  const [transactions, setTransactions] = useState([]);
  const [installmentPlans, setInstallmentPlans] = useState([]);
  const [recurringRules, setRecurringRules] = useState([]);
  const [isLoading, setIsLoading] = useState(true);

  const [loadErrors, setLoadErrors] = useState(null);
  const latestRequest = useRef(0);

  const readAll = useCallback(async () => {
    const request = ++latestRequest.current;
    const signedIn = hasValidSession();
    const [history, installments, recurrences] = await Promise.allSettled(signedIn
      ? [getTransactions(), getInstallmentPlans(), getRecurringRules()]
      : [[], [], []]);
    if (request !== latestRequest.current) return;

    const loaded = (result) => result.status === "fulfilled" && Array.isArray(result.value);
    if (loaded(history)) setTransactions(history.value);
    if (loaded(installments)) setInstallmentPlans(installments.value);
    if (loaded(recurrences)) setRecurringRules(recurrences.value);
    setLoadErrors(signedIn ? {
      history: !loaded(history),
      commitments: !loaded(installments) || !loaded(recurrences),
    } : null);
    setIsLoading(false);
  }, []);

  const loadAll = useCallback(() => {
    setIsLoading(true);
    return readAll();
  }, [readAll]);

  useEffect(() => {
    let active = true;
    queueMicrotask(() => {
      if (active) readAll();
    });
    window.addEventListener("hestia-session-change", loadAll);
    return () => {
      active = false;
      window.removeEventListener("hestia-session-change", loadAll);
    };
  }, [loadAll, readAll]);

  const addTransaction = useCallback(
    async (data) => {
      await createTransaction(data);
      await loadAll();
    },
    [loadAll]
  );

  const importTransactionsBatch = useCallback(
    async (payload) => {
      const result = await importTransactions(payload);
      await loadAll();
      return result;
    },
    [loadAll]
  );

  const removeTransaction = useCallback(
    async (id) => {
      await deleteTransaction(id);
      await loadAll();
    },
    [loadAll]
  );

  const removeInstallmentGroup = useCallback(async (installmentGroupId) => {
    await deleteInstallmentGroup(installmentGroupId);
    setTransactions((current) =>
      current.filter((transaction) => transaction.installmentGroupId !== installmentGroupId)
    );
    setInstallmentPlans((current) => current.filter((plan) => plan.id !== installmentGroupId));
  }, []);

  const updateInstallmentGroupItem = useCallback(
    async (installmentGroupId, data) => {
      await updateInstallmentGroup(installmentGroupId, data);
      await loadAll();
    },
    [loadAll]
  );

  const updateTransactionItem = useCallback(async (id, data) => {
    const updated = await updateTransaction(id, data);
    setTransactions((current) =>
      current.map((transaction) => (transaction.id === id ? updated : transaction))
    );
  }, []);

  const today = currentDayISO();
  const summary = useMemo(
    () => summarizeTransactions(getPostedTransactions(transactions, today)),
    [transactions, today]
  );

  const value = useMemo(
    () => ({
      transactions,
      installmentPlans,
      recurringRules,
      isLoading,
      loadErrors,
      loadAll,
      addTransaction,
      importTransactions: importTransactionsBatch,
      removeTransaction,
      removeInstallmentGroup,
      updateTransaction: updateTransactionItem,
      updateInstallmentGroup: updateInstallmentGroupItem,
      summary,
    }),
    [
      transactions,
      installmentPlans,
      recurringRules,
      isLoading,
      loadErrors,
      loadAll,
      addTransaction,
      importTransactionsBatch,
      removeTransaction,
      removeInstallmentGroup,
      updateTransactionItem,
      updateInstallmentGroupItem,
      summary,
    ]
  );

  return <TransactionsContext.Provider value={value}>{children}</TransactionsContext.Provider>;
}
