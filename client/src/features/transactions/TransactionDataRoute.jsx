import { Outlet } from "react-router-dom";
import RouteLoading from "../../components/RouteLoading";
import { useI18n } from "../../i18n/LanguageProvider";
import { useTransactions } from "./useTransactions";

export default function TransactionDataRoute({ needsCommitments = false }) {
  const { loadErrors, isLoading, loadAll } = useTransactions();
  const { t } = useI18n();
  if (!loadErrors) return <RouteLoading />;

  if (loadErrors.history || (needsCommitments && loadErrors.commitments)) {
    return (
      <section className="hestia-card p-4" role="alert">
        <p>{t(loadErrors.history ? "common.historyReadError" : "common.commitmentsReadError")}</p>
        <button type="button" className="btn btn-primary" disabled={isLoading} onClick={loadAll}>
          {t("common.retryRead")}
        </button>
      </section>
    );
  }
  return <Outlet />;
}
