Localizzazione in lingua, se il progetto presenta stringhe che sono tutte hardcoded, dobbiamo usare una libreria potente che ho già usato altrove. 
Si chiam Tx.Lib e l'ho già aggiunta al progetto dotnet. Ecco come funziona :

Va inizializzata con :
            Tx.UseFileSystemWatcher = true;
            Tx.LoadFromXmlFile(@"localization\languages.txd");
Dove  localization\languages.txd è un file in formato xml che include le traduzioni delle stringhe rispetto ad un alingua principale, che di solito è inglese.

Per cambiare lingua :
Tx.SetCulture(culture);  

Ogni stringa hardcoded nel programma va iniettata con Tx.Text, sia presente nel codice .cs o in XAML.

esempio c#

in ogni .cs va aggiunto l'include l'import : 
using Unclassified.TxLib;
Ovunque ci sia una stringa comq in questo caso :
itemText = "Open in new Tab";
va trasformata in
itemText = Tx.T("Open in new Tab")

esempio xaml 

in ogni xaml va aggiunto il dominio del modulo lib da includere
xmlns:Tx="http://unclassified.software/source/txtranslation"

Ogni riferimento a stringhe come in questo caso
<TextBlock Text="Partially solved" Grid.Row="1" Grid.Column="1" Margin="45,20,0,60"/>

va trasformato in
<TextBlock Text="{Tx:T Partially solved}" Grid.Row="1" Grid.Column="1" Margin="45,20,0,60"/>

--- formato del file languages.txd

<?xml version="1.0" encoding="utf-8"?>
<!-- TxTranslation dictionary file. Use TxEditor to edit this file. http://unclassified.software/txtranslation -->
<translation xml:space="preserve">
	<culture name="de-DE">
    <text key="LOGIN TO KEYCRIME">MELDEN SIE SICH BEI KEYCRIME AN</text>
		<text key="LOGIN">Anmeldung</text>
   	<text key="errors">Fehler</text>
	</culture>
	<culture name="it-IT">
		<text key="LOGIN">ACCEDI</text>
    <text key="Are you sure you want to delete the property '{0}' ?">Sicuro di voler elimnare la proprietà '{0}' ?</text>
    <text key="Are you Sure you want to remove this ">Sei sicuro di voler rimuovere questo </text>
		<text key="Warning">Attenzione</text>
		<text key="LOGIN TO KEYCRIME">ACCEDI A KEYCRIME</text>
    <text key="Create new event">Crea nuovo evento</text>
    <text key="Criminal Analysis and Stats">Analisi criminale&#x0a;e statistiche</text>
   </culture>
 </translation>