function normalizeText(value) {
  return String(value ?? "")
    .normalize("NFD")
    .replace(/[\u0300-\u036f]/g, "")
    .toLowerCase()
    .replace(/\s+/g, " ")
    .trim();
}

function buildFingerprint(transaction) {
  return [
    String(transaction.date ?? "").slice(0, 10),
    transaction.type || "",
    Number(transaction.amountCents) || 0,
    normalizeText(transaction.description),
  ].join("|");
}

export function detectImportDuplicates(previewTransactions, existingTransactions, accountId = null) {
  const sameAccount = (existingTransactions || []).filter(
    (transaction) => (transaction.financialAccountId ?? null) === accountId
  );
  const existingReferences = new Set(
    sameAccount.filter((row) => row.source === "import_ofx").map((row) => row.sourceReference).filter(Boolean)
  );
  const importReferences = new Set();
  const existingFingerprints = new Set(
    sameAccount.map((transaction) => buildFingerprint(transaction))
  );
  const importFingerprints = new Set();

  return (previewTransactions || []).map((transaction) => {
    const fingerprint = buildFingerprint(transaction);
    const reference = transaction.sourceReference;
    const isDuplicateOfExisting = existingFingerprints.has(fingerprint) || existingReferences.has(reference);
    const isDuplicateInsideImport = importFingerprints.has(fingerprint) || importReferences.has(reference);
    if (reference) importReferences.add(reference);

    if (!isDuplicateInsideImport) {
      importFingerprints.add(fingerprint);
    }

    let duplicateReason = "";
    let duplicateSource = "";

    if (isDuplicateOfExisting && isDuplicateInsideImport) {
      duplicateReason =
        "Essa linha já existe no seu histórico e também aparece repetida dentro do arquivo.";
      duplicateSource = "existing_and_import";
    } else if (isDuplicateOfExisting) {
      duplicateReason = "Já existe uma transação muito parecida no seu histórico.";
      duplicateSource = "existing";
    } else if (isDuplicateInsideImport) {
      duplicateReason = "Essa linha parece repetida dentro do próprio arquivo importado.";
      duplicateSource = "import";
    }

    return {
      ...transaction,
      isPossibleDuplicate: isDuplicateOfExisting || isDuplicateInsideImport,
      duplicateSource,
      duplicateReason,
    };
  });
}

export function buildDefaultImportSelection(reconciledTransactions) {
  return new Set(
    (reconciledTransactions || [])
      .map((transaction, index) => ({ transaction, index }))
      .filter(({ transaction }) => !transaction.isPossibleDuplicate)
      .map(({ index }) => index)
  );
}
