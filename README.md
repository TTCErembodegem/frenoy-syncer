# Frenoy Synchronizer

Synchronizer TTC Erembodegem MySql database with the Frenoy WebServices.

HOW TO
------

- Update the FrenoySyncOptions properties for current year
- Run the Frenoy.Syncer
- Configure Service Reference FrenoyVTTL and change from VTTL to Sporta (or vice versa). 
- (Un)comment Sporta/VTTL part
- Run the Frenoy.Syncer again
- Export the tables: reeks, clubploeg, clubploegspeler and kalender

And import on ttc-erembodegem.be...

The WSDL Urls 
-------------
**FrenoyVttlWsdlUrl**: http://api.vttl.be/0.7/?wsdl  
**FrenoySportaWsdlUrl**: http://tafeltennis.sporcrea.be/api/?wsdl

 

