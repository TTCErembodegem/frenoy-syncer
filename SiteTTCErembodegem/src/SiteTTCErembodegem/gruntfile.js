/// <binding BeforeBuild='before-build' AfterBuild='after-build' Clean='clean' ProjectOpened='project-open' />
/*
This file in the main entry point for defining grunt tasks and using grunt plugins.
Click here to learn more. http://go.microsoft.com/fwlink/?LinkID=513275&clcid=0x409
*/
module.exports = function (grunt) {
    grunt.initConfig({
        bower: {
            install: {
                options: {
                    targetDir: "wwwroot/lib",
                    layout: "byComponent",
                    cleanTargetDir: true
                }
            }
        }
    });

    // The following line loads the grunt plugins.
    // This line needs to be at the end of this file.
    grunt.loadNpmTasks("grunt-bower-task");

    grunt.registerTask("before-build", ["bower:install"]);
    grunt.registerTask("after-build", []);
    grunt.registerTask("clean", []);
    grunt.registerTask("project-open", []);
};