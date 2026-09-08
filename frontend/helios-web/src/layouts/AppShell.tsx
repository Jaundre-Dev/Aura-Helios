import type { ReactNode } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { makeStyles, tokens, Caption1, Body1Strong } from '@fluentui/react-components';
import { navigation } from '@/app/navigation';

const useStyles = makeStyles({
  shell: {
    display: 'grid',
    gridTemplateColumns: '248px 1fr',
    gridTemplateRows: '48px 1fr',
    height: '100vh',
    backgroundColor: tokens.colorNeutralBackground2,
  },
  header: {
    gridColumn: '1 / -1',
    display: 'flex',
    alignItems: 'center',
    gap: '16px',
    paddingInline: '16px',
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorNeutralBackground1,
  },
  rail: {
    overflowY: 'auto',
    paddingBlock: '12px',
    borderRight: `1px solid ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorNeutralBackground1,
  },
  group: { paddingInline: '12px', marginBottom: '16px' },
  groupLabel: { display: 'block', paddingInline: '8px', marginBottom: '4px', opacity: 0.6 },
  link: {
    display: 'block',
    paddingInline: '8px',
    paddingBlock: '6px',
    borderRadius: tokens.borderRadiusMedium,
    color: tokens.colorNeutralForeground2,
    textDecoration: 'none',
    fontSize: tokens.fontSizeBase300,
  },
  linkActive: {
    backgroundColor: tokens.colorNeutralBackground1Selected,
    color: tokens.colorNeutralForeground1,
  },
  content: { overflowY: 'auto' },
});

export function AppShell({ children }: { children: ReactNode }) {
  const styles = useStyles();
  const { pathname } = useLocation();

  return (
    <div className={styles.shell}>
      <header className={styles.header}>
        <Body1Strong>HELIOS</Body1Strong>
        <Caption1 style={{ opacity: 0.6 }}>Ctrl+K to search</Caption1>
      </header>

      <nav className={styles.rail}>
        {navigation.map((group) => (
          <div key={group.path} className={styles.group}>
            {group.children ? (
              <Caption1 className={styles.groupLabel}>{group.label.toUpperCase()}</Caption1>
            ) : null}
            {(group.children ?? [group]).map((item) => (
              <Link
                key={item.path}
                to={item.path}
                className={`${styles.link} ${pathname === item.path ? styles.linkActive : ''}`}
              >
                {item.label}
              </Link>
            ))}
          </div>
        ))}
      </nav>

      <main className={styles.content}>{children}</main>
    </div>
  );
}
