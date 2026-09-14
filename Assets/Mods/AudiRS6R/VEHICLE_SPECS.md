# Audi RS6-R vehicle targets

The prefab and runtime tune target the ABT RS6-R based on the C8 Audi RS 6
Avant. The existing driving tune is retained in this update.

| Item | Target |
| --- | ---: |
| Length / width / height | 4,995 / 1,951 / 1,460 mm |
| Wheelbase | 2,929 mm |
| Fuel tank | 73 L |
| Engine | 4.0 L twin-turbo V8 |
| Power / torque | 544 kW (740 hp) / 920 Nm |
| Transmission | Eight-speed automatic, all-wheel drive |
| Top speed | Up to 320 km/h |

The vehicle controller represents the published 544 kW output directly in its
power curve. Its separate forced-induction multiplier therefore remains
disabled to avoid counting turbo boost twice.

Primary references:

- Audi MediaCenter, RS 6 Avant technical data:
  https://www.audi-mediacenter.com/en/audi-rs-6-avant-12084/technical-data
- ABT Sportsline, RS6-R performance specification:
  https://www.abt-sportsline.com/tuning/products/abt-limited-editions/abt-rs6-r
