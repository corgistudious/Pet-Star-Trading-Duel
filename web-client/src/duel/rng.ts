// SplitMix64 identical to shared-contracts/Runtime/Rules/Random.cs (SeededRandom), using BigInt for exact 64-bit math.
const MASK = (1n << 64n) - 1n;
const MAX = MASK;

export function nextULong(state: bigint): [bigint, bigint] {
  const s = (state + 0x9E3779B97F4A7C15n) & MASK;
  let z = s;
  z = ((z ^ (z >> 30n)) * 0xBF58476D1CE4E5B9n) & MASK;
  z = ((z ^ (z >> 27n)) * 0x94D049BB133111EBn) & MASK;
  return [s, z ^ (z >> 31n)];
}

/** Uniform integer in [0, n) with rejection sampling (no modulo bias), same as SeededRandom.Next. Returns [state, value]. */
export function nextInt(state: bigint, n: number): [bigint, number] {
  if (n <= 0) throw new Error('n must be positive');
  const bound = BigInt(n);
  const limit = MAX - (MAX % bound);
  let s = state, r: bigint;
  do { [s, r] = nextULong(s); } while (r >= limit);
  return [s, Number(r % bound)];
}
