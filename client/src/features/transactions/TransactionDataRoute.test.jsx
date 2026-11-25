import { fireEvent, render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { beforeEach, expect, it, vi } from "vitest";
import * as api from "../../lib/api/transactions";
import TransactionDataRoute from "./TransactionDataRoute";
import { TransactionsProvider } from "./TransactionsProvider";
import { useTransactions } from "./useTransactions";

vi.mock("../../lib/api/transactions");
vi.mock("../../lib/api/auth", () => ({ hasValidSession: () => true }));

function History() {
  const { transactions, summary } = useTransactions();
  return <><p>{transactions.length} lançamentos</p><p>Saldo: {summary.balance}</p></>;
}

function renderHistory(needsCommitments = false) {
  render(
    <MemoryRouter>
      <TransactionsProvider>
        <Routes>
          <Route element={<TransactionDataRoute needsCommitments={needsCommitments} />}>
            <Route index element={<History />} />
          </Route>
        </Routes>
      </TransactionsProvider>
    </MemoryRouter>
  );
}

beforeEach(() => {
  api.getTransactions.mockResolvedValue([{ id: 1 }]);
  api.getInstallmentPlans.mockResolvedValue([]);
  api.getRecurringRules.mockResolvedValue([]);
});

it("offers a retry when history is unavailable, then renders the actual count", async () => {
  api.getTransactions.mockRejectedValueOnce(new Error("offline"));
  renderHistory();
  expect(await screen.findByRole("alert")).toHaveTextContent("Não foi possível carregar as transações");
  expect(screen.queryByText("0 lançamentos")).toBeNull();
  fireEvent.click(screen.getByRole("button", { name: "Tentar novamente" }));
  expect(await screen.findByText("1 lançamentos")).toBeVisible();
});

it("blocks incomplete commitments on pages that display them", async () => {
  api.getRecurringRules.mockRejectedValueOnce(new Error("offline"));
  renderHistory(true);
  expect(await screen.findByRole("alert")).toHaveTextContent("parcelas e recorrências");
});

it("still displays history when only the commitments request fails", async () => {
  api.getInstallmentPlans.mockRejectedValueOnce(new Error("offline"));
  renderHistory();
  expect(await screen.findByText("1 lançamentos")).toBeVisible();
});

it("keeps scheduled items in history while excluding them from the posted balance", async () => {
  api.getTransactions.mockResolvedValue([
    { date: "2000-01-01", type: "income", amountCents: 100 },
    { date: "2099-01-01", type: "expense", amountCents: 75 },
  ]);
  renderHistory();
  expect(await screen.findByText("2 lançamentos")).toBeVisible();
  expect(screen.getByText("Saldo: 100")).toBeVisible();
});
