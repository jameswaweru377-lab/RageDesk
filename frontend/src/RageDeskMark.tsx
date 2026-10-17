export default function RageDeskMark() {
  return <svg className="brand-mark" role="img" aria-label="RageDesk logo" viewBox="0 0 64 68" xmlns="http://www.w3.org/2000/svg">
    <defs>
      <linearGradient id="logoBack" x1="0" y1="0" x2="1" y2="1"><stop stopColor="#137BFF"/><stop offset="1" stopColor="#092A9E"/></linearGradient>
      <linearGradient id="logoFront" x1="0" y1="0" x2="1" y2="1"><stop stopColor="#42E9FF"/><stop offset="1" stopColor="#157BFF"/></linearGradient>
      <linearGradient id="logoCore" x1="0" y1="0" x2="1" y2="1"><stop stopColor="#23D5FF"/><stop offset="1" stopColor="#0960EF"/></linearGradient>
    </defs>
    <path d="M4 14 27 2 57 16 57 29 46 35 46 23 27 14 15 20Z" fill="url(#logoBack)"/>
    <path d="M4 14 15 20 15 42 29 35 45 46 61 65 40 65 24 48 15 53 15 65 4 65Z" fill="url(#logoCore)"/>
    <path d="M15 28 46 15 57 21 57 29 26 46 15 42 15 34 45 23 27 14Z" fill="url(#logoFront)"/>
  </svg>;
}
