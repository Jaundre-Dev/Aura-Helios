import { useEffect, useRef } from 'react';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { hubMethods } from '@/signalr/hubRoutes';
import type { HeliosEvent } from '@/types/platform';

/**
 * Plan section 10: state changes reach the UI as events, not as polling.
 * Reconnects automatically; callers re-join their groups in onConnected.
 */
export function useHeliosHub(
  route: string,
  onEvent: (event: HeliosEvent) => void,
  onConnected?: (connection: HubConnection) => void | Promise<void>,
) {
  const connectionRef = useRef<HubConnection | null>(null);

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl(route)
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on(hubMethods.receiveEvent, onEvent);
    connection.onreconnected(() => void onConnected?.(connection));

    void connection.start().then(() => onConnected?.(connection));
    connectionRef.current = connection;

    return () => {
      connection.off(hubMethods.receiveEvent, onEvent);
      void connection.stop();
      connectionRef.current = null;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [route]);

  return connectionRef;
}
