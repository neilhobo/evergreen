import cc from "../styles/ContentCard.module.css";
import PageHeader from "../widgets/PageHeader";

interface Props {
  section: string;
}

// Placeholder card for a nav item whose page is not built yet.
export default function ComingSoon({ section }: Props) {
  return (
    <div className={cc.page}>
      <div className={cc.card}>
        <PageHeader title={section} />
        <div className={cc.comingSoon}>
          <h2>{section}</h2>
          <p>Coming soon</p>
        </div>
      </div>
    </div>
  );
}
