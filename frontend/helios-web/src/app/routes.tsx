import { Route, Routes } from 'react-router-dom';
import { Title2, Body1 } from '@fluentui/react-components';
import { flatNavigation } from '@/app/navigation';

/** Planned API-platform screens; implementation status is explicitly visible. */
function Placeholder({ label }: { label: string }) {
  return (
    <div style={{ padding: '24px 32px' }}>
      <Title2 as="h1">{label}</Title2>
      <Body1 as="p" style={{ display: 'block', marginTop: 8, opacity: 0.7 }}>
        This service platform is under development. This screen is not yet available.
      </Body1>
    </div>
  );
}

export function AppRoutes() {
  return (
    <Routes>
      {flatNavigation.map((item) => (
        <Route key={item.path} path={item.path} element={<Placeholder label={item.label} />} />
      ))}
      <Route path="*" element={<Placeholder label="Not found" />} />
    </Routes>
  );
}
