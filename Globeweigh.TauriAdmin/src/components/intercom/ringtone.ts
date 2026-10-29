// A phone-style ring drawn with Web Audio (no sound file to ship): the UK double ring,
// 400+450 Hz, 0.4s on / 0.2s off / 0.4s on / 2s off, repeating until stopped.

export function startRingtone(): () => void {
  let ctx: AudioContext;
  try {
    ctx = new AudioContext();
  } catch {
    return () => {};
  }
  ctx.resume().catch(() => {});

  const out = ctx.createGain();
  out.gain.value = 0.15;
  out.connect(ctx.destination);

  const burst = (at: number, length: number) => {
    const env = ctx.createGain();
    env.gain.setValueAtTime(0, at);
    env.gain.linearRampToValueAtTime(1, at + 0.01);
    env.gain.setValueAtTime(1, at + length - 0.01);
    env.gain.linearRampToValueAtTime(0, at + length);
    env.connect(out);
    for (const freq of [400, 450]) {
      const osc = ctx.createOscillator();
      osc.frequency.value = freq;
      osc.connect(env);
      osc.start(at);
      osc.stop(at + length);
    }
  };

  const CYCLE = 3;
  let next = ctx.currentTime + 0.05;
  const schedule = () => {
    // Keep one cycle queued ahead of the clock.
    while (next < ctx.currentTime + CYCLE) {
      burst(next, 0.4);
      burst(next + 0.6, 0.4);
      next += CYCLE;
    }
  };
  schedule();
  const timer = window.setInterval(schedule, 1000);

  return () => {
    window.clearInterval(timer);
    ctx.close().catch(() => {});
  };
}
