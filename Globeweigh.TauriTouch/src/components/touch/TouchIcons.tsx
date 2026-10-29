// Monochrome line icons for the touch menu — single-stroke, currentColor, no fill, so they
// take the muted tone set in CSS (accent green on hover) instead of full-colour emoji.
import { ReactNode } from "react";

function Svg({ children }: { children: ReactNode }) {
  return (
    <svg
      width="44"
      height="44"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.5"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      {children}
    </svg>
  );
}

// Boom barrier, raised — Open/Close Barrier
export const IconBarrier = () => (
  <Svg>
    <path d="M3 21h4" />
    <path d="M5 21V8.5" />
    <circle cx="5" cy="6" r="2" />
    <path d="m7 5.3 14-3" />
    <path d="M11 6.1 10.6 4.2" />
    <path d="M15.5 5.1 15.1 3.2" />
  </Svg>
);

// Balance scale — Sample Weight
export const IconScale = () => (
  <Svg>
    <path d="M12 3v16" />
    <path d="M7 19h10" />
    <path d="M5 6h14" />
    <path d="m5 6-2.5 5a2.5 2.5 0 0 0 5 0L5 6Z" />
    <path d="m19 6-2.5 5a2.5 2.5 0 0 0 5 0L19 6Z" />
  </Svg>
);

// Bar chart — Production Summary
export const IconChart = () => (
  <Svg>
    <path d="M4 20V4" />
    <path d="M4 20h16" />
    <rect x="7" y="12" width="3" height="5" />
    <rect x="12" y="8" width="3" height="9" />
    <rect x="17" y="14" width="3" height="3" />
  </Svg>
);

// Factory — Production
export const IconFactory = () => (
  <Svg>
    <path d="M3 20h18" />
    <path d="M4 20V11l6 4V11l6 4V8l4-3v15" />
    <path d="M8 20v-4" />
    <path d="M13 20v-4" />
  </Svg>
);

// Camera
export const IconCamera = () => (
  <Svg>
    <path d="M3 8.5A1.5 1.5 0 0 1 4.5 7h2L8 5h8l1.5 2h2A1.5 1.5 0 0 1 21 8.5v9A1.5 1.5 0 0 1 19.5 19h-15A1.5 1.5 0 0 1 3 17.5v-9Z" />
    <circle cx="12" cy="12.5" r="3.2" />
  </Svg>
);

// Package/box — Delivery (incoming)
export const IconBox = () => (
  <Svg>
    <path d="M12 3 4 7v10l8 4 8-4V7l-8-4Z" />
    <path d="m4 7 8 4 8-4" />
    <path d="M12 11v10" />
    <path d="m8 5 8 4" />
  </Svg>
);

// Stacked boxes — Manual Stock
export const IconBoxes = () => (
  <Svg>
    <rect x="8" y="4" width="8" height="8" />
    <rect x="3" y="13" width="8" height="8" />
    <rect x="13" y="13" width="8" height="8" />
    <path d="M12 4v3" />
    <path d="M7 13v3" />
    <path d="M17 13v3" />
  </Svg>
);

// Truck — Dispatch / Collection
export const IconTruck = () => (
  <Svg>
    <path d="M2 7h11v9H2z" />
    <path d="M13 10h4l3 3v3h-7z" />
    <circle cx="6.5" cy="17.5" r="1.6" />
    <circle cx="17.5" cy="17.5" r="1.6" />
  </Svg>
);
