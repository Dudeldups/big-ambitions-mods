# Audi RS6-R vehicle targets

The prefab and runtime tune target the ABT RS6-R based on the C8 Audi RS 6
Avant. Calibration is driven by bounded in-game acceleration and braking runs.

| Item | Target |
| --- | ---: |
| Length / width / height | 4,995 / 1,951 / 1,460 mm |
| Wheelbase | 2,929 mm |
| Fuel tank | 73 L |
| Engine | 4.0 L twin-turbo V8 |
| Power / torque | 544 kW (740 hp) / 920 Nm |
| Transmission | Eight-speed automatic, all-wheel drive |
| Top speed | Up to 320 km/h |
| 0–100 km/h calibration target | 3.3 s; ABT claim 3.2 s |
| 0–200 km/h calibration target | 10.4 s |
| 100–0 km/h calibration target | 37.5 m |

The vehicle catalog retains the published 544 kW output. The NWH physics engine
uses a separate 345 kW simulation value because its engine-loss field did not
change the measured acceleration response. Forced-induction multiplication
remains disabled to avoid counting turbo boost twice. The remaining calibration
uses a 40:60 front/rear torque split, 780 Nm clutch limit, and a bounded 1,620 Nm
brake input. These values compensate for the simulation's measured 2.59-second
0–100 km/h, 8.36-second 0–200 km/h, and 32.84-metre stop; they are calibration
controls rather than claims about the real car's mechanical losses.

Primary references:

- Audi MediaCenter, RS 6 Avant technical data:
  https://www.audi-mediacenter.com/en/audi-rs-6-avant-12084/technical-data
- ABT Sportsline, RS6-R performance specification:
  https://www.abt-sportsline.com/tuning/products/abt-limited-editions/abt-rs6-r
- AUTO BILD RS6-R test and ABT 0–100 km/h claim:
  https://www.autobild.de/artikel/abt-rs6-r-test-16668155.html
- Auditography instrumented RS6-R acceleration run (3.277 s / 10.44 s):
  https://www.youtube.com/watch?v=FaYPx3yzWlw
- Car and Driver instrumented C8 RS 6 braking baseline:
  https://www.caranddriver.com/reviews/a34631077/2021-audi-rs6-avant-by-the-numbers/
