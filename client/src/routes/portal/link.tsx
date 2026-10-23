import { createFileRoute } from '@tanstack/react-router';
import { PortalFrame } from '@/shared/portal/PortalFrame.tsx';
import { SignedOut } from '@/shared/portal/SignedOut.tsx';

export const Route = createFileRoute('/portal/link')({
  component: LinkPage,
});

function LinkPage() {
  return (
    <PortalFrame>
      <SignedOut />
    </PortalFrame>
  );
}
