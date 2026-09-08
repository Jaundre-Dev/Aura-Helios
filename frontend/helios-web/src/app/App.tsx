import { AppShell } from '@/layouts/AppShell';
import { AppRoutes } from '@/app/routes';

export function App() {
  return (
    <AppShell>
      <AppRoutes />
    </AppShell>
  );
}
