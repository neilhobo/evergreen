interface Props {
  section: string;
}

export default function ComingSoon({ section }: Props) {
  return (
    <div className="coming-soon-wrap">
      <div className="coming-soon">
        <h1>{section}</h1>
        <p>Coming soon</p>
      </div>
    </div>
  );
}
