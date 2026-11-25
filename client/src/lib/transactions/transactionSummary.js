export function currentMonthISO(d = new Date()) {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}`;
}

export function currentDayISO(date = new Date()) {
  return `${currentMonthISO(date)}-${String(date.getDate()).padStart(2, "0")}`;
}

export function getPostedTransactions(transactions, asOf = currentDayISO()) {
  return transactions.filter((transaction) => {
    const day = transaction.date?.slice(0, 10);
    return day && day <= asOf;
  });
}

export function summarizeTransactions(transactions) {
  let income = 0;
  let expense = 0;

  for (const transaction of transactions) {
    const value = Number(transaction.amountCents) || 0;

    if (transaction.type === "income") {
      income += value;
    } else {
      expense += value;
    }
  }

  return {
    income,
    expense,
    balance: income - expense,
  };
}
