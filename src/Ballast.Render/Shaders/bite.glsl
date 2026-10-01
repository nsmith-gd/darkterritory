// A bite (MeshInstance.Bite): a piece eaten away from its back end, as a Car Hugger eats the car it's on (GDD v1.2
// App. A.3 FEED). In the piece's own space (+Z its back, +Y up): everything behind a ragged frontier is gone.
//   bite.x  the back end's z (where the eating starts)
//   bite.y  how deep it's eaten down the middle (m; 0 or less: nothing is)
//   bite.z  how deep at the sides (the hands hold the side walls, so they go last)
//   bite.w  the half width, where the sides are
//   floorY  below this nothing is eaten (the underframe and the trucks: it still rolls until it's dropped)
// The frontier is sines, not noise, so C# (Game/Art/BiteKit) lays the same ragged edge that this cuts.
float biteRagged(float x, float y, float seed) {
    return 0.5 * sin(3.1 * y + seed) + 0.3 * sin(2.3 * x + 1.7 * seed) + 0.2 * sin(7.3 * (y + 0.5 * x) + 2.9 * seed)
        + 0.12 * sin(13.1 * y - 5.0 * x + seed);
}

// How far p is behind the frontier (m): > 0 eaten, < 0 still there.
float biteInto(vec3 p, vec4 bite, float floorY, float seed) {
    if (bite.y <= 0.0 || p.y < floorY)
        return -1e3;
    float centre = 1.0 - smoothstep(bite.w - 0.9, bite.w - 0.1, abs(p.x));
    float depth = mix(bite.z, bite.y, centre);
    // Ragged from the first bite, more so the deeper it goes; never back past the end.
    float amp = min(0.32, 0.12 + 0.08 * bite.y) * clamp(depth / 0.35, 0.0, 1.0);
    depth += amp * (biteRagged(p.x, p.y, seed) * 0.5 + 0.5);
    return p.z - (bite.x - depth);
}
